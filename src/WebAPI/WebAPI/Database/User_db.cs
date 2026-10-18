using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Core user account data access: registration, authentication, email
/// verification, password reset, role assignment/lookup, and admin account-status
/// changes (suspend/ban/reactivate).
/// </summary>
public class User_db
{
    private readonly string _connectionString;

    public User_db(string connectionString) => _connectionString = connectionString;

    private static bool _accountStatusColumnsEnsured = false;

    /// <summary>Adds the uAccountStatus/uStatusReason columns on first use if this DB predates them (in-code migration, checked once per process).</summary>
    private async Task EnsureAccountStatusColumnsAsync(SqlConnection conn)
    {
        if (_accountStatusColumnsEnsured) return;
        try
        {
            string ensureQ = @"IF COL_LENGTH('users', 'uAccountStatus') IS NULL
                                    ALTER TABLE users ADD uAccountStatus NVARCHAR(20) NOT NULL DEFAULT 'Active';
                                IF COL_LENGTH('users', 'uStatusReason') IS NULL
                                    ALTER TABLE users ADD uStatusReason NVARCHAR(MAX) NULL;";
            using var cmd = new SqlCommand(ensureQ, conn);
            await cmd.ExecuteNonQueryAsync();
            _accountStatusColumnsEnsured = true;
        }
        catch { }
    }

    /// <summary>
    /// Persists an admin suspend/ban/reactivate decision. Keeps uIsSuspended in sync
    /// with the new uAccountStatus column so SignIn's existing IsSuspended login-block
    /// check (see AuthenticateUser) also correctly blocks Banned users, not just
    /// Suspended ones - the DB has no separate concept of "Banned" beyond this status
    /// string, both map to uIsSuspended = 1.
    /// </summary>
    /// <summary>Deletes the user's app refresh tokens (AuthTokens), so a suspended user's app can't get a new token.</summary>
    public virtual async Task DeleteAppSessionsAsync(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("DELETE FROM AuthTokens WHERE atUserId = @userId", conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Users with an unexpired app refresh token - the app's "signed in" count for the health panel.</summary>
    public virtual async Task<int> CountAppSessionsAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(DISTINCT atUserId) FROM AuthTokens WHERE atExpired > SYSUTCDATETIME()", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public virtual async Task<bool> UpdateAccountStatus(int userId, string status, string reason)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureAccountStatusColumnsAsync(conn);

        bool isSuspended = status.Equals("Suspended", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Banned", StringComparison.OrdinalIgnoreCase);

        string q = @"UPDATE users SET uAccountStatus = @status, uStatusReason = @reason, uIsSuspended = @isSuspended WHERE userId = @userId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isSuspended", isSuspended);
        cmd.Parameters.AddWithValue("@userId", userId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Creates a user, then a userRoles row and matching role-specific table row
    /// (hunter/farmer/pj/ph/vg/ap) for every requested role (SelectedRoles, or
    /// PrimaryRoleCode, or "Jagter"/Hunter by default). Role codes are normalized
    /// against both Afrikaans and English spellings (JAGTER/HUNTER, BOER/FARMER)
    /// since the roles table may use either. Each role-specific insert is wrapped
    /// in its own try/catch that's silently swallowed - a missing/malformed
    /// role-specific field never blocks the user account itself from being
    /// created, but can leave a userRoles row with no matching role-table row.
    /// PH's daily rates are left NULL rather than defaulted, so a new PH must set
    /// their own pricing. All inserts run in one transaction.
    /// </summary>
    public virtual async Task<bool> RegisterUser(RegisterRequest request)
    {
        try
        {
            if (await UserExists(request.Email))
                throw new Exception("User with this email already exists");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Ensure profile picture and ID columns exist in users table
            try
            {
                string ensureCols = @"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('users') AND name = 'uProfilePictureUrl')
                                      BEGIN
                                          ALTER TABLE users ADD uProfilePictureUrl NVARCHAR(MAX) NULL;
                                      END
                                      IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('users') AND name = 'uIdPassportNum')
                                      BEGIN
                                          ALTER TABLE users ADD uIdPassportNum NVARCHAR(100) NULL;
                                      END";
                using var ensureCmd = new SqlCommand(ensureCols, conn);
                await ensureCmd.ExecuteNonQueryAsync();
            }
            catch { }

            using var tran = conn.BeginTransaction();
            try
            {
                string query = @"INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended, uProfilePictureUrl, uIdPassportNum)
                            VALUES (@uName, @uSurname, @uEmail, @uPassword, @uPhone, @uIsEmailVerified, @uIsPhoneVerified, GETUTCDATE(), @uIsSuspended, @uProfilePictureUrl, @uIdPassportNum);
                            SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(query, conn, tran);
            
                cmd.Parameters.AddWithValue("@uName", request.FirstName);
                cmd.Parameters.AddWithValue("@uSurname", request.Surname);
                cmd.Parameters.AddWithValue("@uEmail", request.Email);
                cmd.Parameters.AddWithValue("@uPassword", HashPassword(request.Password));
                cmd.Parameters.AddWithValue("@uPhone", request.Phone ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@uIsEmailVerified", false);
                cmd.Parameters.AddWithValue("@uIsPhoneVerified", false);
                cmd.Parameters.AddWithValue("@uIsSuspended", false);
                cmd.Parameters.AddWithValue("@uProfilePictureUrl", (object?)request.ProfilePictureUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@uIdPassportNum", (object?)request.PassportOrIdNumber ?? DBNull.Value);
            
                var newUserIdObj = await cmd.ExecuteScalarAsync();
                int newUserId = Convert.ToInt32(newUserIdObj);

                // Determine all roles to assign
                var rolesToAssign = new List<string>();
                if (request.SelectedRoles != null && request.SelectedRoles.Count > 0)
                {
                    rolesToAssign.AddRange(request.SelectedRoles);
                }
                else if (!string.IsNullOrWhiteSpace(request.PrimaryRoleCode))
                {
                    rolesToAssign.Add(request.PrimaryRoleCode);
                }
                else
                {
                    rolesToAssign.Add("Jagter");
                }

                // Deduplicate and process each role
                var distinctRoles = rolesToAssign.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var rawCode in distinctRoles)
                {
                    string code = rawCode.Trim().ToUpperInvariant();

                    // Self-registration can never grant administrator rights (UserController
                    // also rejects this up front; this is the last line of defence).
                    if (code.StartsWith("ADMIN")) continue;

                    string altCode = code switch
                    {
                        "JAGTER" => "HUNTER",
                        "HUNTER" => "JAGTER",
                        "BOER" => "FARMER",
                        "FARMER" => "BOER",
                        _ => code
                    };

                    string getRoleQ = "SELECT TOP 1 roleId FROM roles WHERE UPPER(roleCode) = @code OR UPPER(roleCode) = @altCode";
                    int? roleId = null;
                    using (var roleCmd = new SqlCommand(getRoleQ, conn, tran))
                    {
                        roleCmd.Parameters.AddWithValue("@code", code);
                        roleCmd.Parameters.AddWithValue("@altCode", altCode);
                        var rIdObj = await roleCmd.ExecuteScalarAsync();
                        if (rIdObj != null) roleId = Convert.ToInt32(rIdObj);
                    }

                    if (roleId == null) continue;

                    // Insert into userRoles
                    string insertUrQ = "INSERT INTO userRoles (urUserId, urRoleId) VALUES (@u, @r); SELECT SCOPE_IDENTITY();";
                    int userRoleId = 0;
                    using (var urCmd = new SqlCommand(insertUrQ, conn, tran))
                    {
                        urCmd.Parameters.AddWithValue("@u", newUserId);
                        urCmd.Parameters.AddWithValue("@r", roleId.Value);
                        userRoleId = Convert.ToInt32(await urCmd.ExecuteScalarAsync());
                    }

                    if (userRoleId <= 0) continue;

                    // Insert into role-specific table based on role code
                    if (code == "JAGTER" || code == "HUNTER")
                    {
                        try
                        {
                            string qHunter = "INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio) VALUES (@urId, @country, @uname, @bio)";
                            using var hCmd = new SqlCommand(qHunter, conn, tran);
                            hCmd.Parameters.AddWithValue("@urId", userRoleId);
                            hCmd.Parameters.AddWithValue("@country", "Suid-Afrika");
                            hCmd.Parameters.AddWithValue("@uname", string.IsNullOrWhiteSpace(request.LeaderboardNickname) ? request.FirstName : request.LeaderboardNickname);
                            hCmd.Parameters.AddWithValue("@bio", "Geregistreerde MikPunt Jagter");
                            await hCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                    else if (code == "BOER" || code == "FARMER")
                    {
                        try
                        {
                            string bioSummary = $"Streek: {request.FarmRegion} | Grondbrief: {request.FarmRegNumber}".Trim();
                            string qFarmer = "INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@urId, @name, @reg, @bio)";
                            using var fCmd = new SqlCommand(qFarmer, conn, tran);
                            fCmd.Parameters.AddWithValue("@urId", userRoleId);
                            fCmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(request.FarmName) ? $"{request.FirstName} se Plaas" : request.FarmName);
                            fCmd.Parameters.AddWithValue("@reg", request.FarmRegNumber ?? "");
                            fCmd.Parameters.AddWithValue("@bio", bioSummary);
                            await fCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                    else if (code == "PJ")
                    {
                        try
                        {
                            string bioSummary = $"CIPC: {request.CipcRegNumber} | BTW: {request.TaxVatNumber} | Adres: {request.PhysicalAddress} | Lisensie: {request.PjLicenseNumber} (Verval: {request.PjLicenseExpiry}) | Dok: {request.PjProvinceDocuments}".Trim();
                            string qPj = "INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjYearsOfExperience, pjBio) VALUES (@urId, @name, @reg, @years, @bio)";
                            using var pCmd = new SqlCommand(qPj, conn, tran);
                            pCmd.Parameters.AddWithValue("@urId", userRoleId);
                            pCmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(request.BusinessName) ? $"{request.FirstName} Safari Outfitters" : request.BusinessName);
                            pCmd.Parameters.AddWithValue("@reg", request.CipcRegNumber ?? "");
                            pCmd.Parameters.AddWithValue("@years", 5);
                            pCmd.Parameters.AddWithValue("@bio", bioSummary);
                            await pCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                    else if (code == "PH")
                    {
                        try
                        {
                            string dgText = !string.IsNullOrWhiteSpace(request.DangerousGameEndorsement) ? $"Dangerous Game: {request.DangerousGameEndorsement}" : "Geen Dangerous Game";
                            string bioSummary = $"Lisensie: {request.PhLicenseNumber} ({request.PhIssuingProvince}, Verval: {request.PhLicenseExpiry}) | {dgText} | Vereniging: {request.PhAssociationName} (#{request.PhAssociationMemberNumber}) | Noodhulp: {request.FirstAidCertDetails} (Verval: {request.FirstAidExpiry}) | Vuurwapen: {request.FirearmCompetencyDetails}".Trim();
                            // Daily rates intentionally omitted/NULL here (not the column
                            // DEFAULT 2800/4500) - the PH must actively configure their own
                            // tariffs, not silently inherit a fake default rate.
                            string qPh = "INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio, phStandardDailyRate, phDangerousGameDailyRate) VALUES (@urId, @years, @bio, NULL, NULL)";
                            using var phCmd = new SqlCommand(qPh, conn, tran);
                            phCmd.Parameters.AddWithValue("@urId", userRoleId);
                            phCmd.Parameters.AddWithValue("@years", 5);
                            phCmd.Parameters.AddWithValue("@bio", bioSummary);
                            await phCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                    else if (code == "VG")
                    {
                        try
                        {
                            string droneSummary = $"Hommeltuie: {request.DroneLicenses} | Kwalifikasies: {request.MediaQualifications}".Trim();
                            string qVg = "INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience) VALUES (@urId, @drone, @licence, @cname, @creg, @years)";
                            using var vCmd = new SqlCommand(qVg, conn, tran);
                            vCmd.Parameters.AddWithValue("@urId", userRoleId);
                            vCmd.Parameters.AddWithValue("@drone", !string.IsNullOrWhiteSpace(request.DroneLicenses));
                            vCmd.Parameters.AddWithValue("@licence", droneSummary);
                            vCmd.Parameters.AddWithValue("@cname", string.IsNullOrWhiteSpace(request.BusinessName) ? $"{request.FirstName} Wildsproduksies" : request.BusinessName);
                            vCmd.Parameters.AddWithValue("@creg", request.CipcRegNumber ?? "");
                            vCmd.Parameters.AddWithValue("@years", 3);
                            await vCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                    else if (code == "AP")
                    {
                        try
                        {
                            string qAp = "INSERT INTO ap (apUserRoleId) VALUES (@urId)";
                            using var aCmd = new SqlCommand(qAp, conn, tran);
                            aCmd.Parameters.AddWithValue("@urId", userRoleId);
                            await aCmd.ExecuteNonQueryAsync();
                        }
                        catch { }
                    }
                }

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error registering user: {ex.Message}");
        }

        return true;
    }

    /// <summary>Marks an account's email as verified.</summary>
    public virtual async Task<bool> VerifyEmail(string email)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            string query = "UPDATE users SET uIsEmailVerified = 1 WHERE uEmail = @uEmail";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uEmail", email);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error verifying email: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies email + password and returns the user (with roles populated) on
    /// success, or null. IsEmailVerified is returned exactly as stored, so
    /// UserController.SignIn can refuse an account that never verified its email
    /// (it sends a fresh code instead).
    /// </summary>
    public virtual async Task<User?> AuthenticateUser(LoginRequest request)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = @"SELECT userId, uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended
                       FROM users WHERE uEmail = @uEmail";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uEmail", request.Email);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var user = new User
                {
                    UserId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Surname = reader.GetString(2),
                    Email = reader.GetString(3),
                    Phone = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsEmailVerified = reader.GetBoolean(6),
                    IsPhoneVerified = reader.GetBoolean(7),
                    RegistrationDate = reader.GetDateTime(8),
                    IsSuspended = reader.GetBoolean(9)
                };

                string storedPasswordHash = reader.GetString(4);
                reader.Close();

                var (valid, needsRehash) = WebAPI.Services.PasswordHasher.Verify(request.Password, storedPasswordHash);
                if (valid)
                {
                    // Old unsalted SHA-256 / plain rows are upgraded the first time the
                    // right password is used. Best effort: a failed upgrade must not block login.
                    if (needsRehash)
                    {
                        try
                        {
                            using var upd = new SqlCommand("UPDATE users SET uPassword = @p WHERE userId = @id", conn);
                            upd.Parameters.AddWithValue("@p", HashPassword(request.Password));
                            upd.Parameters.AddWithValue("@id", user.UserId);
                            await upd.ExecuteNonQueryAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[User_db] Could not upgrade password hash for user {user.UserId}: {ex.Message}");
                        }
                    }
                    user.Roles = await GetUserRoles(user.UserId) ?? new List<UserRole>();
                    return user;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error authenticating user: {ex.Message}");
        }
    }

    /// <summary>Fetches a user (with roles populated) by email, without password verification.</summary>
    public virtual Task<User?> GetUserByEmail(string email)
        => GetUserWhereAsync("uEmail = @value", email, "email");

    /// <summary>Fetches a user (with roles populated) by id.</summary>
    public virtual Task<User?> GetUserById(int userId)
        => GetUserWhereAsync("userId = @value", userId, "id");

    /// <summary>The one user matching <paramref name="where"/> (on @value), with roles; null when none.</summary>
    private async Task<User?> GetUserWhereAsync(string where, object value, string lookup)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = $@"SELECT userId, uName, uSurname, uEmail, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended
                              FROM users WHERE {where}";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@value", value);

            User user;
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return null;
                user = new User
                {
                    UserId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Surname = reader.GetString(2),
                    Email = reader.GetString(3),
                    Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsEmailVerified = reader.GetBoolean(5),
                    IsPhoneVerified = reader.GetBoolean(6),
                    RegistrationDate = reader.GetDateTime(7),
                    IsSuspended = reader.GetBoolean(8)
                };
            }
            user.Roles = await GetUserRoles(user.UserId);
            return user;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting user by {lookup}: {ex.Message}");
        }
    }

    /// <summary>Lists every role in the system.</summary>
    public virtual async Task<List<Role>> GetAvailableRoles()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT roleId, roleCode, roleName FROM roles ORDER BY roleId";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Role>();
        while (await r.ReadAsync())
        {
            list.Add(new Role
            {
                RoleId = r.GetInt32(0),
                RoleCode = r.GetString(1),
                RoleName = r.GetString(2)
            });
        }
        return list;
    }

    /// <summary>Whether an account with this email already exists - used to reject duplicate registrations.</summary>
    public virtual async Task<bool> UserExists(string email)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users WHERE uEmail = @uEmail";
            cmd.Parameters.AddWithValue("@uEmail", email);
            
            int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return count > 0;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error checking if user exists: {ex.Message}");
        }
    }

    /// <summary>Lists every role assigned to a user, with role code/name resolved via a LEFT JOIN (so a dangling/unmatched urRoleId still returns a row, just with null role fields).</summary>
    public virtual async Task<List<UserRole>> GetUserRoles(int userId)
    {
        try
        {
            var roles = new List<UserRole>();
            
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            
            string query = @"SELECT ur.userRoleId, ur.urUserId, ur.urRoleId, r.roleCode, r.roleName
                           FROM userRoles ur
                           LEFT JOIN roles r ON ur.urRoleId = r.roleId
                           WHERE ur.urUserId = @userId";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                roles.Add(new UserRole
                {
                    UserRoleId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    RoleId = reader.GetInt32(2),
                    RoleCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleName = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
            
            return roles;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting user roles: {ex.Message}");
        }
    }
    
    /// <summary>Grants a role to a user, idempotently (returns true without inserting a duplicate if already assigned). Returns false if roleCode doesn't exist.</summary>
    public virtual async Task<bool> AssignRoleToUser(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        string getRoleId = "SELECT roleId FROM roles WHERE roleCode = @roleCode";
        using var roleCmd = new SqlCommand(getRoleId, conn);
        roleCmd.Parameters.AddWithValue("@roleCode", roleCode.ToUpper());
        var roleId = await roleCmd.ExecuteScalarAsync();
        
        if (roleId == null)
            return false;
        
        string checkQ = "SELECT COUNT(*) FROM userRoles WHERE urUserId = @userId AND urRoleId = @roleId";
        using var checkCmd = new SqlCommand(checkQ, conn);
        checkCmd.Parameters.AddWithValue("@userId", userId);
        checkCmd.Parameters.AddWithValue("@roleId", (int)roleId);
        int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        
        if (exists > 0)
            return true;
        
        string insertQ = "INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId)";
        using var insertCmd = new SqlCommand(insertQ, conn);
        insertCmd.Parameters.AddWithValue("@userId", userId);
        insertCmd.Parameters.AddWithValue("@roleId", (int)roleId);
        
        return await insertCmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Generic reader for a role-specific table's row (all columns, dynamically), keyed by the role table's foreign key to userRoles rather than its own PK.</summary>
    public virtual async Task<List<Dictionary<string, object?>>> GetUserDataRole(int userRoleId, string roleID)
    {
        // FK column, not each table's own PK - userRoleId is a userRoles.userRoleId
        // value, and each role table's own IDENTITY PK (hunterId/phId/etc.) is a
        // separate, unrelated auto-increment sequence. Filtering on the PK instead of
        // the FK (as this used to) meant the two ID spaces only lined up by accident,
        // returning empty results or a different, wrong role-holder's row.
        var roleTable = new Dictionary<string, (string TableName, string ForeignKeyColumn)>
        {
            { "HUNTER", ("hunter", "hUserRoleId") },
            { "PH", ("ph", "phUserRoleId") },
            { "PJ", ("pj", "pjUserRoleId") },
            { "FARMER", ("farmer", "fUserRoleId") },
            { "VG", ("vg", "vgUserRoleId") },
            { "AP", ("ap", "apUserRoleId") },
            { "ADMIN", ("admin", "adminUserRoleId") }
        };

        if (!roleTable.TryGetValue(roleID.ToUpper(), out var tableInfo))
            throw new ArgumentException($"Invalid role: {roleID}");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = $"SELECT * FROM {tableInfo.TableName} WHERE {tableInfo.ForeignKeyColumn} = @roleID";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@roleID", userRoleId);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Dictionary<string, object?>>();
        
        while (await reader.ReadAsync())
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                dict[reader.GetName(i)] = reader.GetValue(i) == DBNull.Value ? null : reader.GetValue(i);
            }
            list.Add(dict);
        }
        
        return list;
    }

    /// <summary>Salted PBKDF2 (see Services.PasswordHasher).</summary>
    private static string HashPassword(string password) => WebAPI.Services.PasswordHasher.Hash(password);

    /// <summary>Full user listing with account status and roles, for the admin dashboard's user-management view.</summary>
    public virtual async Task<List<User>> GetAllUsers()
    {
        try
        {
            var users = new List<User>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await EnsureAccountStatusColumnsAsync(conn);

            string query = @"
            SELECT userId, uName, uSurname, uEmail, uPhone,
                   uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended,
                   uAccountStatus, uStatusReason
            FROM users";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var user = new User
                {
                    UserId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Surname = reader.GetString(2),
                    Email = reader.GetString(3),
                    Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsEmailVerified = reader.GetBoolean(5),
                    IsPhoneVerified = reader.GetBoolean(6),
                    RegistrationDate = reader.GetDateTime(7),
                    IsSuspended = reader.GetBoolean(8),
                    AccountStatus = reader.IsDBNull(9) ? null : reader.GetString(9),
                    StatusReason = reader.IsDBNull(10) ? null : reader.GetString(10)
                };

                users.Add(user);
            }

            reader.Close();

            // Load roles for each user (optional but consistent with your design)
            foreach (var user in users)
            {
                user.Roles = await GetUserRoles(user.UserId);
            }

            return users;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting all users: {ex.Message}");
        }
    }

    /// <summary>Sets a new (hashed) password for the account, called at the end of the reset-password flow.</summary>
    public virtual async Task<bool> UpdatePassword(string email, string newPassword)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            
            string query = "UPDATE users SET uPassword = @uPassword WHERE uEmail = @uEmail";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uPassword", HashPassword(newPassword));
            cmd.Parameters.AddWithValue("@uEmail", email);
            
            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error updating password: {ex.Message}");
        }
    }
}
           

