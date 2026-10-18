using Microsoft.Data.SqlClient;
using WebAPI.Models;
using WebAPI.Services;

namespace WebAPI.Database;

/// <summary>
/// Core user-profile and role-registration data access: base profile CRUD,
/// role grant/revoke, the per-role registration get/update pairs (PJ, PH,
/// Farmer, VG, Hunter, AP), PH day-rate pricing, and per-role subscription
/// management/payment.
/// </summary>
public class Profile_db
{
    private readonly string _connectionString;
    private readonly ISystemSettingsCache? _settingsCache;

    public Profile_db(string connectionString) : this(connectionString, null) { }

    public Profile_db(string connectionString, ISystemSettingsCache? settingsCache)
    {
        _connectionString = connectionString;
        _settingsCache = settingsCache;
    }

    /// <summary>Adds the uProfilePictureUrl/uIdPassportNum columns on first use if this DB predates them (in-code migration).</summary>
    private async Task EnsureProfilePictureColumnAsync(SqlConnection conn)
    {
        try
        {
            string checkCol = @"IF COL_LENGTH('users', 'uProfilePictureUrl') IS NULL
                               BEGIN
                                   ALTER TABLE users ADD uProfilePictureUrl NVARCHAR(MAX);
                               END;
                               IF COL_LENGTH('users', 'uIdPassportNum') IS NULL
                               BEGIN
                                   ALTER TABLE users ADD uIdPassportNum NVARCHAR(MAX);
                               END;";
            using var cmd = new SqlCommand(checkCol, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    /// <summary>Fetches a user's base profile with their assigned roles.</summary>
    public virtual async Task<ProfileResponse?> GetProfile(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureProfilePictureColumnAsync(conn);
      
        string q = @"SELECT u.userId, u.uName, u.uSurname, u.uEmail, u.uPhone, u.uRegistrationDate, u.uProfilePictureUrl, u.uIdPassportNum
                     FROM users u WHERE u.userId = @userId";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        
        ProfileResponse? profile = null;

        using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                profile = new ProfileResponse
                {
                    UserId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Surname = r.GetString(2),
                    Email = r.GetString(3),
                    Phone = r.IsDBNull(4) ? null : r.GetString(4),
                    Registered = r.GetDateTime(5),
                    ProfilePictureUrl = r.IsDBNull(6) ? null : r.GetString(6),
                    IdPassportNum = r.IsDBNull(7) ? null : r.GetString(7)
                };
            }
        }

        if (profile != null)
        {
            string rolesQ = @"SELECT ur.userRoleId, ur.urUserId, r.roleCode, r.roleName
                             FROM userRoles ur
                             JOIN roles r ON ur.urRoleId = r.roleId
                             WHERE ur.urUserId = @userId";
            
            using var rolesCmd = new SqlCommand(rolesQ, conn);
            rolesCmd.Parameters.AddWithValue("@userId", userId);
            
            using var rolesReader = await rolesCmd.ExecuteReaderAsync();
            while (await rolesReader.ReadAsync())
            {
                profile.Roles.Add(new UserRoleData
                {
                    Id = rolesReader.GetInt32(0),
                    UserId = rolesReader.GetInt32(1),
                    RoleCode = rolesReader.GetString(2),
                    RoleName = rolesReader.GetString(3),
                    Status = "Active",
                    AssignedDate = DateTime.UtcNow
                });
            }
        }

        return profile;
    }

    /// <summary>Partial update of a user's base profile fields - only non-null request fields are written.</summary>
    public virtual async Task<bool> UpdateProfile(int userId, UpdateProfileRequest request)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object)>();
        
        if (request.Name != null)
        {
            sets.Add("uName = @name");
            parameters.Add(("@name", request.Name));
        }
        if (request.Surname != null)
        {
            sets.Add("uSurname = @surname");
            parameters.Add(("@surname", request.Surname));
        }
        if (request.Phone != null)
        {
            sets.Add("uPhone = @phone");
            parameters.Add(("@phone", request.Phone));
        }
        if (request.IdPassportNum != null)
        {
            sets.Add("uIdPassportNum = @idNum");
            parameters.Add(("@idNum", request.IdPassportNum));
        }
        if (request.ProfilePictureUrl != null)
        {
            sets.Add("uProfilePictureUrl = @profilePic");
            parameters.Add(("@profilePic", request.ProfilePictureUrl));
        }
        
        if (sets.Count == 0) return false;
        
        string q = $"UPDATE users SET {string.Join(",", sets)} WHERE userId = @userId";
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureProfilePictureColumnAsync(conn);
        using var cmd = new SqlCommand(q, conn);
        
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@userId", userId);
        
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>
    /// Grants a role, matching against Afrikaans/English aliases (HUNTER/JAGTER,
    /// FARMER/BOER, AP/ADVERTISER). Returns the new userRoleId, 0 if roleCode
    /// doesn't resolve to any role, or -1 if the user already holds it (an
    /// "already assigned" sentinel, not an error - callers use it to fall back to
    /// GetUserRoleId for the existing assignment).
    /// </summary>
    public virtual async Task<int> AddRole(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        // First get the roleId supporting aliases (HUNTER/JAGTER, FARMER/BOER, AP/ADVERTISER)
        string code = (roleCode ?? "").Trim().ToUpperInvariant();
        string altCode = code switch
        {
            "JAGTER" => "HUNTER",
            "HUNTER" => "JAGTER",
            "BOER" => "FARMER",
            "FARMER" => "BOER",
            "ADVERTISER" or "ADVERTEERDER" => "AP",
            "AP" => "ADVERTISER",
            _ => code
        };

        string getRole = @"SELECT TOP 1 roleId FROM roles 
                          WHERE UPPER(roleCode) = @code 
                             OR UPPER(roleCode) = @altCode 
                             OR (@code = 'AP' AND (roleName LIKE '%Advert%' OR roleName = 'Advertensie Plaser'))";
        using var cmd = new SqlCommand(getRole, conn);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@altCode", altCode);
        var roleId = await cmd.ExecuteScalarAsync();
        
        if (roleId == null) return 0;
        
        // Check if already exists
        string checkQ = "SELECT COUNT(*) FROM userRoles WHERE urUserId = @u AND urRoleId = @r";
        using var checkCmd = new SqlCommand(checkQ, conn);
        checkCmd.Parameters.AddWithValue("@u", userId);
        checkCmd.Parameters.AddWithValue("@r", (int)roleId);
        int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        
        if (exists > 0) return -1;
        
        string insert = "INSERT INTO userRoles (urUserId, urRoleId) VALUES (@u, @r); SELECT SCOPE_IDENTITY();";
        using var cmd2 = new SqlCommand(insert, conn);
        cmd2.Parameters.AddWithValue("@u", userId);
        cmd2.Parameters.AddWithValue("@r", (int)roleId);
        var newid = await cmd2.ExecuteScalarAsync();
        return Convert.ToInt32(newid);
    }

    /// <summary>Resolves a user's userRoleId for a role code (same alias matching as AddRole). Returns 0 if not found.</summary>
    public virtual async Task<int> GetUserRoleId(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string code = (roleCode ?? "").Trim().ToUpperInvariant();
        string altCode = code switch
        {
            "JAGTER" => "HUNTER",
            "HUNTER" => "JAGTER",
            "BOER" => "FARMER",
            "FARMER" => "BOER",
            "ADVERTISER" or "ADVERTEERDER" => "AP",
            "AP" => "ADVERTISER",
            _ => code
        };

        string getUrQ = @"SELECT TOP 1 ur.userRoleId 
                          FROM userRoles ur
                          JOIN roles r ON ur.urRoleId = r.roleId
                          WHERE ur.urUserId = @userId 
                            AND (UPPER(r.roleCode) = @code 
                                 OR UPPER(r.roleCode) = @altCode
                                 OR (@code = 'AP' AND (r.roleName LIKE '%Advert%' OR r.roleName = 'Advertensie Plaser')))";
        
        using var cmd = new SqlCommand(getUrQ, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@altCode", altCode);

        var urIdObj = await cmd.ExecuteScalarAsync();
        return urIdObj != null ? Convert.ToInt32(urIdObj) : 0;
    }

    /// <summary>Removes a role by code (same alias matching as AddRole/GetUserRoleId), delegating the actual cascade to DeleteRole.</summary>
    public virtual async Task<bool> DeleteRoleByCode(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string code = (roleCode ?? "").Trim().ToUpperInvariant();
        string altCode = code switch
        {
            "JAGTER" => "HUNTER",
            "HUNTER" => "JAGTER",
            "BOER" => "FARMER",
            "FARMER" => "BOER",
            "ADVERTISER" or "ADVERTEERDER" => "AP",
            "AP" => "ADVERTISER",
            _ => code
        };

        string getUrQ = @"SELECT ur.userRoleId 
                          FROM userRoles ur
                          JOIN roles r ON ur.urRoleId = r.roleId
                          WHERE ur.urUserId = @userId 
                            AND (UPPER(r.roleCode) = @code 
                                 OR UPPER(r.roleCode) = @altCode
                                 OR (@code = 'AP' AND (r.roleName LIKE '%Advert%' OR r.roleName = 'Advertensie Plaser')))";
        
        using var cmd = new SqlCommand(getUrQ, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@altCode", altCode);

        var urIdObj = await cmd.ExecuteScalarAsync();
        if (urIdObj == null) return false;

        int userRoleId = Convert.ToInt32(urIdObj);
        return await DeleteRole(userId, userRoleId);
    }

    /// <summary>
    /// Removes a role assignment, manually cascading through every table with a
    /// dependency on that userRoleId first (team invites/memberships, documents -
    /// soft-deleted and detached rather than hard-deleted so their audit trail
    /// survives, AP/VG/PH/PJ/Farmer/Hunter-specific rows, bookings references) since
    /// the schema doesn't do this via FK cascade. Each step is independently
    /// try/caught so one missing/already-clean table never blocks the rest.
    /// </summary>
    public virtual async Task<bool> DeleteRole(int userId, int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Team Invites & Team records (matches by userRoleId)
        try
        {
            string delTeam = @"DELETE FROM teamInvites WHERE invitePjId = @urId OR inviteMemberId = @urId;
                               DELETE FROM team WHERE teamPjId = @urId OR teamMemberId = @urId;";
            using var cmd = new SqlCommand(delTeam, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 2. User documentation - soft-delete and detach from the role being removed.
        // Hard-deleting is impossible: documentAuditLog is append-only (FK'd to
        // userDocumentation, blocked from direct delete by its own triggers), and the
        // audit trail must survive the role's deletion regardless. udUserRole is
        // nullable, so detaching it here lets step 10's userRoles delete succeed
        // while the document rows - and their full audit history - remain in the
        // database for compliance.
        try
        {
            string delDoc = "UPDATE userDocumentation SET udIsDeleted = 1, udUserRole = NULL WHERE udUserRole = @urId;";
            using var cmd = new SqlCommand(delDoc, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 3. AP dependencies (advertisements -> ap)
        try
        {
            string delAp = @"DELETE FROM advertisements WHERE adApId IN (SELECT apId FROM ap WHERE apUserRoleId = @urId);
                             DELETE FROM ap WHERE apUserRoleId = @urId;";
            using var cmd = new SqlCommand(delAp, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 4. VG dependencies (vgPackages, vgAvailableProvinces, reset bookVgId in bookings -> vg)
        try
        {
            string delVg = @"DELETE FROM vgPackages WHERE vgId IN (SELECT vgId FROM vg WHERE vgUserRoleId = @urId);
                             DELETE FROM vgAvailableProvinces WHERE vgapVgId IN (SELECT vgId FROM vg WHERE vgUserRoleId = @urId);
                             UPDATE bookings SET bookVgId = NULL WHERE bookVgId IN (SELECT vgId FROM vg WHERE vgUserRoleId = @urId);
                             DELETE FROM vg WHERE vgUserRoleId = @urId;";
            using var cmd = new SqlCommand(delVg, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 5. PH dependencies (phPricing, reset bookPhId in bookings -> ph)
        try
        {
            string delPh = @"DELETE FROM phPricing WHERE phpPhId IN (SELECT phId FROM ph WHERE phUserRoleId = @urId);
                             UPDATE bookings SET bookPhId = NULL WHERE bookPhId IN (SELECT phId FROM ph WHERE phUserRoleId = @urId);
                             DELETE FROM ph WHERE phUserRoleId = @urId;";
            using var cmd = new SqlCommand(delPh, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 6. PJ dependencies (storyDataPoint -> storyData -> bookings -> packages -> pj)
        try
        {
            string delPj = @"DELETE FROM storyDataPoint WHERE sdpStoryDataId IN (
                                SELECT storyDataId FROM storyData WHERE sdBookingId IN (
                                    SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                        SELECT packageId FROM packages WHERE pPjId IN (
                                            SELECT pjId FROM pj WHERE pjUserRoleId = @urId
                                        )
                                    )
                                )
                             );
                             DELETE FROM storyData WHERE sdBookingId IN (
                                 SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                     SELECT packageId FROM packages WHERE pPjId IN (
                                         SELECT pjId FROM pj WHERE pjUserRoleId = @urId
                                     )
                                 )
                             );
                             DELETE FROM ratings WHERE bookingId IN (
                                 SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                     SELECT packageId FROM packages WHERE pPjId IN (
                                         SELECT pjId FROM pj WHERE pjUserRoleId = @urId
                                     )
                                 )
                             );
                             DELETE FROM bookings WHERE bookPackageId IN (
                                 SELECT packageId FROM packages WHERE pPjId IN (
                                     SELECT pjId FROM pj WHERE pjUserRoleId = @urId
                                 )
                             );
                             DELETE FROM packages WHERE pPjId IN (SELECT pjId FROM pj WHERE pjUserRoleId = @urId);
                             DELETE FROM pj WHERE pjUserRoleId = @urId;";
            using var cmd = new SqlCommand(delPj, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 7. Farmer / Boer dependencies (storyDataPoint -> storyData -> bookings -> packages, farmLodge, availableAnimals -> farm -> farmer)
        try
        {
            string delFarmer = @"DELETE FROM storyDataPoint WHERE sdpStoryDataId IN (
                                     SELECT storyDataId FROM storyData WHERE sdBookingId IN (
                                         SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                             SELECT packageId FROM packages WHERE pFarmId IN (
                                                 SELECT farmId FROM farm WHERE farmFarmerId IN (
                                                     SELECT farmerId FROM farmer WHERE fUserRoleId = @urId
                                                 )
                                             )
                                         )
                                     )
                                 );
                                 DELETE FROM storyData WHERE sdBookingId IN (
                                     SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                         SELECT packageId FROM packages WHERE pFarmId IN (
                                             SELECT farmId FROM farm WHERE farmFarmerId IN (
                                                 SELECT farmerId FROM farmer WHERE fUserRoleId = @urId
                                             )
                                         )
                                     )
                                 );
                                 DELETE FROM ratings WHERE bookingId IN (
                                     SELECT bookingId FROM bookings WHERE bookPackageId IN (
                                         SELECT packageId FROM packages WHERE pFarmId IN (
                                             SELECT farmId FROM farm WHERE farmFarmerId IN (
                                                 SELECT farmerId FROM farmer WHERE fUserRoleId = @urId
                                             )
                                         )
                                     )
                                 );
                                 DELETE FROM bookings WHERE bookPackageId IN (
                                     SELECT packageId FROM packages WHERE pFarmId IN (
                                         SELECT farmId FROM farm WHERE farmFarmerId IN (
                                             SELECT farmerId FROM farmer WHERE fUserRoleId = @urId
                                         )
                                     )
                                 );
                                 DELETE FROM packages WHERE pFarmId IN (SELECT farmId FROM farm WHERE farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @urId));
                                 DELETE FROM farmLodge WHERE flFarmId IN (SELECT farmId FROM farm WHERE farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @urId));
                                 DELETE FROM availableAnimals WHERE aaFarmId IN (SELECT farmId FROM farm WHERE farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @urId));
                                 DELETE FROM farm WHERE farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @urId);
                                 DELETE FROM farmer WHERE fUserRoleId = @urId;";
            using var cmd = new SqlCommand(delFarmer, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 8. Hunter dependencies (storyDataPoint -> storyData -> bookings where bookHunterId = @urId -> hunter where hUserRoleId = @urId)
        try
        {
            string delHunter = @"DELETE FROM storyDataPoint WHERE sdpStoryDataId IN (
                                     SELECT storyDataId FROM storyData WHERE sdBookingId IN (
                                         SELECT bookingId FROM bookings WHERE bookHunterId = @urId
                                     )
                                 );
                                 DELETE FROM storyData WHERE sdBookingId IN (
                                     SELECT bookingId FROM bookings WHERE bookHunterId = @urId
                                 );
                                 DELETE FROM ratings WHERE bookingId IN (
                                     SELECT bookingId FROM bookings WHERE bookHunterId = @urId
                                 );
                                 DELETE FROM bookings WHERE bookHunterId = @urId;
                                 DELETE FROM hunter WHERE hUserRoleId = @urId;";
            using var cmd = new SqlCommand(delHunter, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 9. Admin dependencies
        try
        {
            string delAdmin = "DELETE FROM admin WHERE adminUserRoleId = @urId;";
            using var cmd = new SqlCommand(delAdmin, conn);
            cmd.Parameters.AddWithValue("@urId", userRoleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 10. Finally delete from userRoles table
        string q = "DELETE FROM userRoles WHERE urUserId = @u AND userRoleId = @r";
        using var finalCmd = new SqlCommand(q, conn);
        finalCmd.Parameters.AddWithValue("@u", userId);
        finalCmd.Parameters.AddWithValue("@r", userRoleId);
        int rows = await finalCmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>
    /// Permanently deletes a user and everything that depends on them: ratings,
    /// every role's dependencies (via DeleteRole, which already handles
    /// userDocumentation correctly), auth tokens, chat activity, and the userRoles
    /// rows themselves. Each step is independently try/caught, so one failure
    /// never blocks the rest. The documents step is a safety net for any document
    /// DeleteRole left behind (userDocumentation is keyed by udUserRole).
    /// </summary>
    public virtual async Task<bool> DeleteUser(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // 0. Ratings this user gave or received, on any booking - must go before the
        // role/booking cleanup below since ratings FK-references bookings.
        try
        {
            string delRating = "DELETE FROM ratings WHERE raterUserId = @u OR rateeUserId = @u";
            using var cmd = new SqlCommand(delRating, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 1. Find all userRoles for this user
        var userRoleIds = new List<int>();
        string getRolesQuery = "SELECT userRoleId FROM userRoles WHERE urUserId = @u";
        using (var getRolesCmd = new SqlCommand(getRolesQuery, conn))
        {
            getRolesCmd.Parameters.AddWithValue("@u", userId);
            using var reader = await getRolesCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                userRoleIds.Add(reader.GetInt32(0));
            }
        }

        // 2. Delete dependencies for every role
        foreach (var roleId in userRoleIds)
        {
            await DeleteRole(userId, roleId);
        }

        // 3. Delete user-level dependencies
        try
        {
            string delAuth = "DELETE FROM AuthTokens WHERE atUserId = @u";
            using var cmd = new SqlCommand(delAuth, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        try
        {
            string delChat = @"DELETE FROM chatMessages WHERE cmSenderId = @u;
                               DELETE FROM chatParticipants WHERE cpUserId = @u;";
            using var cmd = new SqlCommand(delChat, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        try
        {
            string delDocs = "DELETE FROM userDocumentation WHERE udUserRole IN (SELECT userRoleId FROM userRoles WHERE urUserId = @u)";
            using var cmd = new SqlCommand(delDocs, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        try
        {
            string delContact = "DELETE FROM contactUs WHERE cUserId = @u";
            using var cmd = new SqlCommand(delContact, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        try
        {
            string delRoles = "DELETE FROM userRoles WHERE urUserId = @u";
            using var cmd = new SqlCommand(delRoles, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }

        // 4. Finally delete the user record
        string delUser = "DELETE FROM users WHERE userId = @u";
        using var finalCmd = new SqlCommand(delUser, conn);
        finalCmd.Parameters.AddWithValue("@u", userId);
        int rows = await finalCmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>Upserts a PJ's registration details (update if a pj row exists for this userRoleId, insert otherwise).</summary>
    public virtual async Task<bool> RegisterPj(int userRoleId, RegisterPjRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string bioSummary = $"CIPC: {request.BusinessRegistrationNumber} | BTW/Tax: {request.TaxPinVat} | Adres: {request.PhysicalAddress} | Provinsiale Lisensies: {request.ProvincialLicenses} | {request.Bio}".Trim();
        string q = @"IF EXISTS (SELECT 1 FROM pj WHERE pjUserRoleId = @urId)
                     BEGIN
                         UPDATE pj SET pjBusinessName = @name, pjBusinessRegistrationNumber = @reg, pjYearsOfExperience = @years, pjBio = @bio
                         WHERE pjUserRoleId = @urId
                     END
                     ELSE
                     BEGIN
                         INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjYearsOfExperience, pjBio) VALUES (@urId, @name, @reg, @years, @bio)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@name", request.BusinessName);
        cmd.Parameters.AddWithValue("@reg", request.BusinessRegistrationNumber);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        cmd.Parameters.AddWithValue("@bio", bioSummary);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Upserts a PH's registration details - daily rates are left NULL on first insert so a new PH must configure their own pricing.</summary>
    public virtual async Task<bool> RegisterPh(int userRoleId, RegisterPhRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);
        string dgText = request.HasDangerousGameEndorsement ? $"Dangerous Game: {request.DangerousGameDetails}" : "Geen Dangerous Game";
        string faExpiry = request.FirstAidExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A";
        string bioSummary = $"Lisensies: {request.ProvincialLicenses} | {dgText} | Vereniging: {request.AssociationName} (#{request.AssociationMembershipNumber}) | Noodhulp: {request.FirstAidCertification} (Verval: {faExpiry}) | Vuurwapenbevoegdheid: {request.FirearmCompetencyLicenses} | {request.Bio}".Trim();
        string q = @"IF EXISTS (SELECT 1 FROM ph WHERE phUserRoleId = @urId)
                     BEGIN
                         UPDATE ph SET phYearsOfExperience = @years, phBio = @bio
                         WHERE phUserRoleId = @urId
                     END
                     ELSE
                     BEGIN
                         -- Daily rates are explicitly NULL here, not the column DEFAULT
                         -- 2800/4500, so a new PH is unconfigured and must actively set
                         -- their own tariffs rather than silently inheriting a fake rate.
                         INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio, phStandardDailyRate, phDangerousGameDailyRate) VALUES (@urId, @years, @bio, NULL, NULL)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        cmd.Parameters.AddWithValue("@bio", bioSummary);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Upserts a Farmer's registration details.</summary>
    public virtual async Task<bool> RegisterFarmer(int userRoleId, RegisterBoerRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string bioSummary = $"Streek: {request.Region} | {request.Bio}".Trim();
        string q = @"IF EXISTS (SELECT 1 FROM farmer WHERE fUserRoleId = @urId)
                     BEGIN
                         UPDATE farmer SET fBusinessName = @name, fBusinessRegistrationNumber = @reg, fBio = @bio
                         WHERE fUserRoleId = @urId
                     END
                     ELSE
                     BEGIN
                         INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@urId, @name, @reg, @bio)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@name", request.BusinessName);
        cmd.Parameters.AddWithValue("@reg", request.BusinessRegistrationNumber);
        cmd.Parameters.AddWithValue("@bio", bioSummary);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Upserts a VG's registration details.</summary>
    public virtual async Task<bool> RegisterVg(int userRoleId, RegisterVgRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string droneSummary = $"SACAA: {request.SacaaRegistrationNumber} | RPL: {request.RplLicenseNumber} | ROC: {request.RocCertificateNumber} | {request.DroneLicence}".Trim();
        string q = @"IF EXISTS (SELECT 1 FROM vg WHERE vgUserRoleId = @urId)
                     BEGIN
                         UPDATE vg SET vgHasDrone = @drone, vgDroneLicence = @licence, vgCompanyName = @cname, vgCompanyRegistrationNumber = @creg, vgYearsOfExperience = @years
                         WHERE vgUserRoleId = @urId
                     END
                     ELSE
                     BEGIN
                         INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience) VALUES (@urId, @drone, @licence, @cname, @creg, @years)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@drone", request.HasDrone);
        cmd.Parameters.AddWithValue("@licence", droneSummary);
        cmd.Parameters.AddWithValue("@cname", request.CompanyName);
        cmd.Parameters.AddWithValue("@creg", request.CompanyRegistrationNumber);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Upserts a Hunter's registration details, plus (best-effort, separately) their passport/ID number onto the users table.</summary>
    public virtual async Task<bool> RegisterHunter(int userRoleId, RegisterHunterRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"IF EXISTS (SELECT 1 FROM hunter WHERE hUserRoleId = @urId)
                     BEGIN
                         UPDATE hunter SET hCountry = @country, hUsername = @uname, hBio = @bio
                         WHERE hUserRoleId = @urId
                     END
                     ELSE
                     BEGIN
                         INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio) VALUES (@urId, @country, @uname, @bio)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@country", request.Country);
        cmd.Parameters.AddWithValue("@uname", request.Username);
        cmd.Parameters.AddWithValue("@bio", request.Bio);
        var res = await cmd.ExecuteNonQueryAsync() > 0;

        if (!string.IsNullOrWhiteSpace(request.IdPassportNumber))
        {
            try
            {
                await EnsureProfilePictureColumnAsync(conn);
                string updateIdQ = @"UPDATE users SET uIdPassportNum = @idNum 
                                     WHERE userId = (SELECT urUserId FROM userRoles WHERE userRoleId = @urId)";
                using var idCmd = new SqlCommand(updateIdQ, conn);
                idCmd.Parameters.AddWithValue("@idNum", request.IdPassportNumber);
                idCmd.Parameters.AddWithValue("@urId", userRoleId);
                await idCmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        return res;
    }

    /// <summary>
    /// Creates the ap row for this role if missing (AP has no other fields to
    /// register). Returns false both on failure and on the harmless case where
    /// the row already existed - the guarded INSERT then affects 0 rows, which
    /// ExecuteNonQueryAsync reports identically to an actual failure.
    /// </summary>
    public virtual async Task<bool> RegisterAp(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"IF NOT EXISTS (SELECT 1 FROM ap WHERE apUserRoleId = @urId)
                     BEGIN
                         INSERT INTO ap (apUserRoleId) VALUES (@urId)
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Fetches a PJ's registration details.</summary>
    public virtual async Task<RegisterPjRequest?> GetPj(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT pjBusinessName, pjBusinessRegistrationNumber, pjYearsOfExperience, pjBio FROM pj WHERE pjUserRoleId = @urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RegisterPjRequest
            {
                BusinessName = r.IsDBNull(0) ? "" : r.GetString(0),
                BusinessRegistrationNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                YearsOfExperience = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)),
                Bio = r.IsDBNull(3) ? "" : r.GetString(3)
            };
        }
        return null;
    }

    /// <summary>Full overwrite of a PJ's registration details (unlike RegisterPj's upsert, this assumes the row already exists).</summary>
    public virtual async Task<bool> UpdatePj(int userRoleId, RegisterPjRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE pj SET pjBusinessName=@name, pjBusinessRegistrationNumber=@reg, pjYearsOfExperience=@years, pjBio=@bio WHERE pjUserRoleId=@urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@name", (object?)request.BusinessName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reg", (object?)request.BusinessRegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        cmd.Parameters.AddWithValue("@bio", (object?)request.Bio ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Fetches a PH's registration details (years of experience and bio only - see GetPhDailyRates for pricing).</summary>
    public virtual async Task<RegisterPhRequest?> GetPh(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT phYearsOfExperience, phBio FROM ph WHERE phUserRoleId = @urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RegisterPhRequest
            {
                YearsOfExperience = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)),
                Bio = r.IsDBNull(1) ? "" : r.GetString(1)
            };
        }
        return null;
    }

    /// <summary>Full overwrite of a PH's registration details (assumes the row already exists).</summary>
    public virtual async Task<bool> UpdatePh(int userRoleId, RegisterPhRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE ph SET phYearsOfExperience=@years, phBio=@bio WHERE phUserRoleId=@urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        cmd.Parameters.AddWithValue("@bio", (object?)request.Bio ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Fetches a Farmer's registration details.</summary>
    public virtual async Task<RegisterBoerRequest?> GetFarmer(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT fBusinessName, fBusinessRegistrationNumber, fBio FROM farmer WHERE fUserRoleId = @urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RegisterBoerRequest
            {
                BusinessName = r.IsDBNull(0) ? "" : r.GetString(0),
                BusinessRegistrationNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                Bio = r.IsDBNull(2) ? "" : r.GetString(2)
            };
        }
        return null;
    }

    /// <summary>Full overwrite of a Farmer's registration details (assumes the row already exists).</summary>
    public virtual async Task<bool> UpdateFarmer(int userRoleId, RegisterBoerRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE farmer SET fBusinessName=@name, fBusinessRegistrationNumber=@reg, fBio=@bio WHERE fUserRoleId=@urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@name", (object?)request.BusinessName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reg", (object?)request.BusinessRegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bio", (object?)request.Bio ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Fetches a VG's registration details.</summary>
    public virtual async Task<RegisterVgRequest?> GetVg(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience FROM vg WHERE vgUserRoleId = @urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RegisterVgRequest
            {
                HasDrone = r.IsDBNull(0) ? false : r.GetBoolean(0),
                DroneLicence = r.IsDBNull(1) ? "" : r.GetString(1),
                CompanyName = r.IsDBNull(2) ? "" : r.GetString(2),
                CompanyRegistrationNumber = r.IsDBNull(3) ? "" : r.GetString(3),
                YearsOfExperience = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4))
            };
        }
        return null;
    }

    /// <summary>Full overwrite of a VG's registration details (assumes the row already exists).</summary>
    public virtual async Task<bool> UpdateVg(int userRoleId, RegisterVgRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE vg SET vgHasDrone=@drone, vgDroneLicence=@licence, vgCompanyName=@cname, vgCompanyRegistrationNumber=@creg, vgYearsOfExperience=@years WHERE vgUserRoleId=@urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@drone", request.HasDrone);
        cmd.Parameters.AddWithValue("@licence", (object?)request.DroneLicence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cname", (object?)request.CompanyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@creg", (object?)request.CompanyRegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@years", request.YearsOfExperience);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Fetches a Hunter's registration details.</summary>
    public virtual async Task<RegisterHunterRequest?> GetHunter(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT hCountry, hUsername, hBio FROM hunter WHERE hUserRoleId = @urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RegisterHunterRequest
            {
                Country = r.IsDBNull(0) ? "" : r.GetString(0),
                Username = r.IsDBNull(1) ? "" : r.GetString(1),
                Bio = r.IsDBNull(2) ? "" : r.GetString(2)
            };
        }
        return null;
    }

    /// <summary>Full overwrite of a Hunter's registration details (assumes the row already exists).</summary>
    public virtual async Task<bool> UpdateHunter(int userRoleId, RegisterHunterRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE hunter SET hCountry=@country, hUsername=@uname, hBio=@bio WHERE hUserRoleId=@urId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@urId", userRoleId);
        cmd.Parameters.AddWithValue("@country", (object?)request.Country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uname", (object?)request.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bio", (object?)request.Bio ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Adds the PH daily-rate columns (with 2800/4500 column DEFAULTs that only
    /// ever apply to a row inserted without specifying them - RegisterPh always
    /// inserts an explicit NULL, so those defaults never actually fire in practice)
    /// and creates the roleSubscriptions table, on first use.
    /// </summary>
    private async Task EnsureSubscriptionAndPhSchemaAsync(SqlConnection conn)
    {
        try
        {
            string checkCol = @"IF COL_LENGTH('ph', 'phStandardDailyRate') IS NULL
                               BEGIN
                                   ALTER TABLE ph ADD phStandardDailyRate DECIMAL(18,2) NULL DEFAULT 2800;
                               END;
                               IF COL_LENGTH('ph', 'phDangerousGameDailyRate') IS NULL
                               BEGIN
                                   ALTER TABLE ph ADD phDangerousGameDailyRate DECIMAL(18,2) NULL DEFAULT 4500;
                               END;
                               IF COL_LENGTH('ph', 'phHasDangerousGame') IS NULL
                               BEGIN
                                   ALTER TABLE ph ADD phHasDangerousGame BIT NULL DEFAULT 1;
                               END;
                               IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'roleSubscriptions')
                               BEGIN
                                   CREATE TABLE roleSubscriptions (
                                       subId INT IDENTITY(1,1) PRIMARY KEY,
                                       subUserId INT NOT NULL,
                                       subRoleCode NVARCHAR(50) NOT NULL,
                                       subIsActive BIT NOT NULL DEFAULT 0,
                                       subExpiryDate DATETIME2 NULL,
                                       subLastPaidAmount DECIMAL(18,2) NULL,
                                       subLastPaymentReference NVARCHAR(100) NULL,
                                       subActiveDurationMonths INT NULL DEFAULT 0,
                                       subLastPaidDate DATETIME2 NULL,
                                       subPaymentStatus NVARCHAR(50) NULL DEFAULT 'Unpaid'
                                   );
                               END;
                               IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_roleSubscriptions_UserRole')
                                   CREATE UNIQUE INDEX UQ_roleSubscriptions_UserRole ON roleSubscriptions (subUserId, subRoleCode);";
            using var cmd = new SqlCommand(checkCol, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    /// <summary>
    /// Fetches a PH's day rates. Note: HasDangerousGameAccreditation defaults to
    /// TRUE when the column is NULL (`r.IsDBNull(2) || Convert.ToBoolean(...)`) -
    /// a PH with no accreditation data on record reads as accredited, which is the
    /// opposite of a fail-safe default.
    /// </summary>
    public virtual async Task<PhDailyRatesDto> GetPhDailyRates(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);

        string q = @"SELECT ph.phStandardDailyRate, ph.phDangerousGameDailyRate, ph.phHasDangerousGame 
                     FROM ph 
                     JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId 
                     WHERE ur.urUserId = @userId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new PhDailyRatesDto
            {
                UserId = userId,
                StandardDailyRate = r.IsDBNull(0) ? null : r.GetDecimal(0),
                DangerousGameDailyRate = r.IsDBNull(1) ? null : r.GetDecimal(1),
                HasDangerousGameAccreditation = r.IsDBNull(2) || Convert.ToBoolean(r.GetValue(2))
            };
        }
        return new PhDailyRatesDto { UserId = userId };
    }

    /// <summary>Upserts a PH's day rates, resolving the PH's userRoleId from their userId first (matching either "PH" or the Afrikaans role name).</summary>
    public virtual async Task<bool> SavePhDailyRates(int userId, decimal standardRate, decimal dangerousGameRate, bool hasDangerousGame)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);

        string q = @"DECLARE @urId INT = (SELECT TOP 1 userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @userId AND (r.roleCode = 'PH' OR r.roleCode = 'Professionele Jagter'));
                     IF @urId IS NOT NULL
                     BEGIN
                         IF EXISTS (SELECT 1 FROM ph WHERE phUserRoleId = @urId)
                         BEGIN
                             UPDATE ph SET phStandardDailyRate = @std, phDangerousGameDailyRate = @dg, phHasDangerousGame = @hasDg
                             WHERE phUserRoleId = @urId;
                         END
                         ELSE
                         BEGIN
                             INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio, phStandardDailyRate, phDangerousGameDailyRate, phHasDangerousGame)
                             VALUES (@urId, 1, 'MikPunt PH Gids', @std, @dg, @hasDg);
                         END
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@std", standardRate);
        cmd.Parameters.AddWithValue("@dg", dangerousGameRate);
        cmd.Parameters.AddWithValue("@hasDg", hasDangerousGame);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Fetches a role subscription's status, or an unpaid, inactive default if none exists yet.
    /// </summary>
    public virtual async Task<RoleSubscriptionDto> GetRoleSubscription(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);

        decimal defaultFee = roleCode switch
        {
            "PH" => _settingsCache?.GetDecimal("SubscriptionPrice_PH", 299m) ?? 299m,
            "VG" => _settingsCache?.GetDecimal("SubscriptionPrice_VG", 249m) ?? 249m,
            "Boer" => _settingsCache?.GetDecimal("SubscriptionPrice_Boer", 499m) ?? 499m,
            "PJ" => _settingsCache?.GetDecimal("SubscriptionPrice_PJ", 699m) ?? 699m,
            _ => _settingsCache?.GetDecimal("SubscriptionPrice_PH", 299m) ?? 299m
        };

        string q = @"SELECT subIsActive, subExpiryDate, subLastPaidAmount, subLastPaymentReference, subActiveDurationMonths, subLastPaidDate, subPaymentStatus
                     FROM roleSubscriptions
                     WHERE subUserId = @userId AND subRoleCode = @roleCode";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleCode", roleCode);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new RoleSubscriptionDto
            {
                UserId = userId,
                RoleCode = roleCode,
                IsActive = !r.IsDBNull(0) && Convert.ToBoolean(r.GetValue(0)),
                ExpiryDate = r.IsDBNull(1) ? null : r.GetDateTime(1),
                LastPaidAmount = r.IsDBNull(2) ? null : r.GetDecimal(2),
                LastPaymentReference = r.IsDBNull(3) ? null : r.GetString(3),
                ActiveDurationMonths = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                LastPaidDate = r.IsDBNull(5) ? null : r.GetDateTime(5),
                PaymentStatus = r.IsDBNull(6) ? "Unpaid" : r.GetString(6),
                MonthlyFee = defaultFee
            };
        }

        // No row = never paid. (Demo users 1/2/4/5 used to read as paid here, which made
        // them look like subscribers everywhere without ever paying.)
        return new RoleSubscriptionDto
        {
            UserId = userId,
            RoleCode = roleCode,
            IsActive = false,
            MonthlyFee = defaultFee,
            TierName = "Standard Advertiser",
            PaymentStatus = "Unpaid"
        };
    }

    /// <summary>
    /// The owner pausing or resuming their own listing. Resuming only works while a paid period
    /// is still running (subExpiryDate in the future), so this can never stand in for paying.
    /// False when there's no subscription row, or when resuming without a paid period.
    /// </summary>
    public virtual async Task<bool> SetSubscriptionActive(int userId, string roleCode, bool active)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);
        const string q = @"UPDATE roleSubscriptions SET subIsActive = @active
                           WHERE subUserId = @userId AND subRoleCode = @roleCode
                             AND (@active = 0 OR (subExpiryDate > SYSUTCDATETIME() AND subPaymentStatus = 'Paid'))";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@active", active);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleCode", roleCode);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Upserts a role subscription record wholesale (full overwrite, not partial) - prefer ProcessSubscriptionPayment for crediting an actual payment, which extends rather than overwrites the expiry.</summary>
    public virtual async Task<bool> SaveRoleSubscription(RoleSubscriptionDto sub)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);

        string q = @"IF EXISTS (SELECT 1 FROM roleSubscriptions WHERE subUserId = @userId AND subRoleCode = @roleCode)
                     BEGIN
                         UPDATE roleSubscriptions 
                         SET subIsActive = @active, 
                             subExpiryDate = @expiry, 
                             subLastPaidAmount = @amount, 
                             subLastPaymentReference = @refNum, 
                             subActiveDurationMonths = @dur, 
                             subLastPaidDate = @paidDate, 
                             subPaymentStatus = @status
                         WHERE subUserId = @userId AND subRoleCode = @roleCode;
                     END
                     ELSE
                     BEGIN
                         INSERT INTO roleSubscriptions (subUserId, subRoleCode, subIsActive, subExpiryDate, subLastPaidAmount, subLastPaymentReference, subActiveDurationMonths, subLastPaidDate, subPaymentStatus)
                         VALUES (@userId, @roleCode, @active, @expiry, @amount, @refNum, @dur, @paidDate, @status);
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", sub.UserId);
        cmd.Parameters.AddWithValue("@roleCode", sub.RoleCode);
        cmd.Parameters.AddWithValue("@active", sub.IsActive);
        cmd.Parameters.AddWithValue("@expiry", (object?)sub.ExpiryDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amount", (object?)sub.LastPaidAmount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@refNum", (object?)sub.LastPaymentReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dur", sub.ActiveDurationMonths);
        cmd.Parameters.AddWithValue("@paidDate", (object?)sub.LastPaidDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)sub.PaymentStatus ?? "Unpaid");
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Credits a subscription payment (extends ExpiryDate by durationMonths, stacking
    /// on top of any unexpired remainder). Idempotent on paymentRef: a reloaded
    /// PayFast return URL carries the same pf_payment_id on every reload, so if this
    /// exact paymentRef was already recorded as Paid for this user+role, this is a
    /// no-op that returns the unchanged subscription instead of double-crediting
    /// days - the same double-payment hole as booking payments (see
    /// Bookings_db.SetPaymentStatus), but here the wrong outcome is silently
    /// extending someone's subscription for free rather than a stuck status flag.
    /// The check-then-act runs under one locked transaction (UPDLOCK, HOLDLOCK on the
    /// read; UQ_roleSubscriptions_UserRole as a hard backstop) so two concurrent
    /// calls for the same reference can't both pass the check and both extend.
    /// </summary>
    public virtual async Task<RoleSubscriptionDto> ProcessSubscriptionPayment(int userId, string roleCode, int durationMonths, decimal amount, string paymentRef)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureSubscriptionAndPhSchemaAsync(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            string checkQ = @"SELECT subLastPaymentReference, subPaymentStatus, subExpiryDate
                               FROM roleSubscriptions WITH (UPDLOCK, HOLDLOCK)
                               WHERE subUserId = @userId AND subRoleCode = @roleCode";
            string? existingRef = null, existingStatus = null;
            DateTime? existingExpiry = null;
            using (var checkCmd = new SqlCommand(checkQ, conn, tx))
            {
                checkCmd.Parameters.AddWithValue("@userId", userId);
                checkCmd.Parameters.AddWithValue("@roleCode", roleCode);
                using var r = await checkCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    existingRef = r.IsDBNull(0) ? null : r.GetString(0);
                    existingStatus = r.IsDBNull(1) ? null : r.GetString(1);
                    existingExpiry = r.IsDBNull(2) ? null : r.GetDateTime(2);
                }
            }

            bool isDuplicatePayment = !string.IsNullOrWhiteSpace(paymentRef)
                && string.Equals(existingStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                && string.Equals(existingRef, paymentRef, StringComparison.Ordinal);

            if (!isDuplicatePayment)
            {
                DateTime baseDate = existingExpiry.HasValue && existingExpiry.Value > DateTime.UtcNow
                    ? existingExpiry.Value
                    : DateTime.UtcNow;

                DateTime newExpiry = baseDate.AddDays(durationMonths * 30);

                const string upsertQ = @"MERGE INTO roleSubscriptions WITH (HOLDLOCK) AS target
                    USING (SELECT @userId AS subUserId, @roleCode AS subRoleCode) AS src
                        ON target.subUserId = src.subUserId AND target.subRoleCode = src.subRoleCode
                    WHEN MATCHED THEN
                        UPDATE SET subIsActive = 1, subExpiryDate = @expiry, subLastPaidAmount = @amount,
                                   subLastPaymentReference = @refNum, subActiveDurationMonths = @dur,
                                   subLastPaidDate = @paidDate, subPaymentStatus = 'Paid'
                    WHEN NOT MATCHED THEN
                        INSERT (subUserId, subRoleCode, subIsActive, subExpiryDate, subLastPaidAmount, subLastPaymentReference, subActiveDurationMonths, subLastPaidDate, subPaymentStatus)
                        VALUES (@userId, @roleCode, 1, @expiry, @amount, @refNum, @dur, @paidDate, 'Paid');";
                using var cmd = new SqlCommand(upsertQ, conn, tx);
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@roleCode", roleCode);
                cmd.Parameters.AddWithValue("@expiry", newExpiry);
                cmd.Parameters.AddWithValue("@amount", amount);
                cmd.Parameters.AddWithValue("@refNum", (object?)paymentRef ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dur", durationMonths);
                cmd.Parameters.AddWithValue("@paidDate", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // Read the final, persisted state back after the transaction has committed -
        // correct for both branches (duplicate: unchanged; applied: freshly extended)
        // and, crucially, kept outside the try/catch above: GetRoleSubscription opens
        // its own connection, and calling it while still inside that try/catch would
        // mean a failure here hits `catch { tx.Rollback(); ... }` on an
        // ALREADY-COMMITTED transaction, throwing a misleading "This SqlTransaction
        // has completed" instead of the real error.
        return await GetRoleSubscription(userId, roleCode);
    }
}

