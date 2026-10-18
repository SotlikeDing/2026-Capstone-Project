using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// PJ team management: team membership CRUD, candidate browsing, the invite/
/// respond workflow, member removal (with booking-conflict checks and an audit
/// trail in teamAudit), and the shared in-app notification bell (userNotifications
/// - also written to by Bookings_db and AdminController).
/// </summary>
public class Team_db
{
    private readonly string _connectionString;
    private static bool _tableEnsured = false;
    private static readonly object _lock = new();

    public Team_db(string connectionString)
    {
        _connectionString = connectionString;
        EnsureTableExists();
    }

    /// <summary>Creates the teamInvites/teamAudit/userNotifications tables (and the notification index) on first use, thread-safe via the lock above.</summary>
    private void EnsureTableExists()
    {
        if (_tableEnsured) return;
        lock (_lock)
        {
            if (_tableEnsured) return;
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                string sql = @"
                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='teamInvites' AND xtype='U')
                    BEGIN
                        CREATE TABLE teamInvites (
                            inviteId     INT IDENTITY(1,1) PRIMARY KEY,
                            invitePjId   INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
                            inviteMemberId INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
                            inviteStatus NVARCHAR(20) NOT NULL DEFAULT 'Pending',
                            inviteDate   DATETIME2 NOT NULL DEFAULT GETDATE(),
                            CONSTRAINT UQ_TeamInvite UNIQUE (invitePjId, inviteMemberId)
                        );
                    END

                    IF COL_LENGTH('dbo.teamInvites', 'inviteExpeditionName') IS NULL
                        ALTER TABLE dbo.teamInvites ADD inviteExpeditionName NVARCHAR(200) NULL;
                    IF COL_LENGTH('dbo.teamInvites', 'inviteTerms') IS NULL
                        ALTER TABLE dbo.teamInvites ADD inviteTerms NVARCHAR(1000) NULL;

                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='teamAudit' AND xtype='U')
                    BEGIN
                        CREATE TABLE teamAudit (
                            auditId             INT IDENTITY(1,1) PRIMARY KEY,
                            teamId              INT NOT NULL,
                            pjUserId            INT NOT NULL,
                            pjUserRoleId        INT NOT NULL,
                            memberUserId        INT NOT NULL,
                            memberUserRoleId    INT NOT NULL,
                            memberRoleCode      NVARCHAR(50) NULL,
                            actionType          NVARCHAR(50) NOT NULL,
                            reasonCode          NVARCHAR(100) NULL,
                            reasonDescription   NVARCHAR(500) NULL,
                            notes               NVARCHAR(MAX) NULL,
                            hadUpcomingBookings BIT NOT NULL DEFAULT 0,
                            affectedBookingIds  NVARCHAR(MAX) NULL,
                            timestamp           DATETIME2 NOT NULL DEFAULT GETDATE()
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='userNotifications' AND xtype='U')
                    BEGIN
                        CREATE TABLE userNotifications (
                            notificationId      INT IDENTITY(1,1) PRIMARY KEY,
                            recipientUserId     INT NOT NULL,
                            senderUserId        INT NULL,
                            notificationType    NVARCHAR(50) NOT NULL,
                            title               NVARCHAR(200) NOT NULL,
                            message             NVARCHAR(MAX) NOT NULL,
                            actionUrl           NVARCHAR(500) NULL,
                            isRead              BIT NOT NULL DEFAULT 0,
                            createdTimestamp    DATETIME2 NOT NULL DEFAULT GETDATE(),
                            readTimestamp       DATETIME2 NULL
                        );
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_userNotifications_recipient')
                        CREATE INDEX IX_userNotifications_recipient ON dbo.userNotifications (recipientUserId, isRead);";
                using var cmd = new SqlCommand(sql, conn);
                cmd.ExecuteNonQuery();
                _tableEnsured = true;
            }
            catch
            {
                // Silently ignore if connection fails during design-time/tests
            }
        }
    }

    /// <summary>
    /// Lists a PJ's team members. teamPjId is matched against several possible
    /// shapes of "PJ identity" (a userRoleId directly, a pjId resolved via
    /// userRoleId, or a raw userId) since callers have historically passed
    /// different id types here.
    /// </summary>
    public virtual async Task<List<TeamMemberResponse>> GetTeamMembers(int teamPjId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT t.teamId, t.teamMemberId, u.userId, u.uName, u.uSurname, ro.roleCode
                     FROM team t
                     JOIN userRoles ur ON t.teamMemberId = ur.userRoleId
                     JOIN users u ON ur.urUserId = u.userId
                     JOIN roles ro ON ur.urRoleId = ro.roleId
                     WHERE t.teamPjId IN (SELECT urPj.userRoleId FROM userRoles urPj WHERE urPj.urUserId = @pj)";
        
        using var cmd = new SqlCommand(q, conn); 
        cmd.Parameters.AddWithValue("@pj", teamPjId);
        using var r = await cmd.ExecuteReaderAsync(); 
        var list = new List<TeamMemberResponse>();
        
        while (await r.ReadAsync())
        {
            list.Add(new TeamMemberResponse
            {
                Id = r.GetInt32(0),
                MemberUserRoleId = r.GetInt32(1),
                UserId = r.GetInt32(2),
                Name = r.GetString(3),
                Surname = r.GetString(4),
                Role = r.GetString(5)
            });
        }
        return list;
    }

    /// <summary>Fetches a single team-membership record by its own team table id.</summary>
    public virtual async Task<TeamMemberResponse?> GetTeamMember(int id)
    {
        using var conn = new SqlConnection(_connectionString); 
        await conn.OpenAsync();
        string q = @"SELECT t.teamId, t.teamMemberId, u.userId, u.uName, u.uSurname, ro.roleCode
                     FROM team t
                     JOIN userRoles ur ON t.teamMemberId = ur.userRoleId
                     JOIN users u ON ur.urUserId = u.userId
                     JOIN roles ro ON ur.urRoleId = ro.roleId
                     WHERE t.teamId = @id";
        
        using var cmd = new SqlCommand(q, conn); 
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        
        if (await r.ReadAsync())
        {
            return new TeamMemberResponse
            {
                Id = r.GetInt32(0),
                MemberUserRoleId = r.GetInt32(1),
                UserId = r.GetInt32(2),
                Name = r.GetString(3),
                Surname = r.GetString(4),
                Role = r.GetString(5)
            };
        }
        return null;
    }

    /// <summary>Directly inserts a team-membership row (bypassing the invite/accept flow) and returns its generated id.</summary>
    public virtual async Task<int> AddTeamMember(AddTeamMemberRequest request)
    {
        using var conn = new SqlConnection(_connectionString); 
        await conn.OpenAsync();
        string q = "INSERT INTO team (teamPjId, teamMemberId) VALUES (@p, @m); SELECT SCOPE_IDENTITY();";
        using var cmd = new SqlCommand(q, conn); 
        cmd.Parameters.AddWithValue("@p", request.TeamPjId); 
        cmd.Parameters.AddWithValue("@m", request.MemberId);
        
        var id = await cmd.ExecuteScalarAsync(); 
        return Convert.ToInt32(id);
    }

    /// <summary>Removes a team-membership row directly, with none of ExecuteTeamMemberRemoval's conflict checks or notifications.</summary>
    public virtual async Task<bool> DeleteTeamMember(int id)
    {
        using var conn = new SqlConnection(_connectionString); 
        await conn.OpenAsync();
        string q = "DELETE FROM team WHERE teamId = @id";
        using var cmd = new SqlCommand(q, conn); 
        cmd.Parameters.AddWithValue("@id", id);
        
        int rows = await cmd.ExecuteNonQueryAsync(); 
        return rows > 0;
    }

    /// <summary>
    /// Candidate PH/VG/Farmer profiles a PJ can browse to invite onto their team,
    /// filtered by role codes and requiring a verified (email), non-suspended
    /// account. Unlike People_db.GetPeople's public discovery, this does NOT gate
    /// on document-verification status - a PJ can find and invite a candidate here
    /// even if that candidate wouldn't show up in public search yet.
    /// </summary>
    public virtual async Task<List<BrowseProfileResponse>> BrowseByRoles(List<string> roleCodes)
    {
        if (roleCodes.Contains("Boer") && !roleCodes.Contains("FARMER"))
            roleCodes.Add("FARMER");

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // roleSubscriptions is created lazily by Profile_db; before that nobody has a paid listing.
        bool hasSubscriptions;
        using (var check = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.roleSubscriptions') IS NULL THEN 0 ELSE 1 END", conn))
            hasSubscriptions = Convert.ToInt32(await check.ExecuteScalarAsync()) == 1;
        // Subscriptions are stored under 'Boer' for the FARMER role.
        string subscription = hasSubscriptions
            ? @"OUTER APPLY (SELECT TOP 1 rs.subIsActive, rs.subExpiryDate FROM roleSubscriptions rs
                             WHERE rs.subUserId = u.userId
                               AND rs.subRoleCode = CASE WHEN UPPER(r.roleCode) IN ('FARMER', 'BOER') THEN 'Boer' ELSE r.roleCode END
                             ORDER BY rs.subExpiryDate DESC) s"
            : "OUTER APPLY (SELECT CAST(NULL AS BIT) AS subIsActive, CAST(NULL AS DATETIME2) AS subExpiryDate) s";

        var inClause = string.Join(",", roleCodes.Select((_, i) => $"@r{i}"));
        string q = $@"SELECT ur.userRoleId, u.userId, u.uName, u.uSurname, u.uEmail, r.roleCode, r.roleName,
                             COALESCE(pj.pjBusinessName, f.fBusinessName, vg.vgCompanyName, '') as businessName,
                             COALESCE(pj.pjBio, ph.phBio, f.fBio, '') as bio,
                             COALESCE(pj.pjYearsOfExperience, ph.phYearsOfExperience, vg.vgYearsOfExperience, 0) as yearsExperience,
                             ISNULL(u.uPhone, '') as phone,
                             CAST(CASE WHEN s.subIsActive = 1 AND s.subExpiryDate > SYSUTCDATETIME() THEN 1 ELSE 0 END AS BIT) as subscriptionActive,
                             s.subExpiryDate
                      FROM userRoles ur
                      JOIN users u ON ur.urUserId = u.userId
                      JOIN roles r ON ur.urRoleId = r.roleId
                      LEFT JOIN pj ON ur.userRoleId = pj.pjUserRoleId
                      LEFT JOIN ph ON ur.userRoleId = ph.phUserRoleId
                      LEFT JOIN vg ON ur.userRoleId = vg.vgUserRoleId
                      LEFT JOIN farmer f ON ur.userRoleId = f.fUserRoleId
                      {subscription}
                      WHERE r.roleCode IN ({inClause})
                      AND u.uIsEmailVerified = 1
                      AND u.uIsSuspended = 0
                      ORDER BY u.uName";

        using var cmd = new SqlCommand(q, conn);
        for (int i = 0; i < roleCodes.Count; i++)
            cmd.Parameters.AddWithValue($"@r{i}", roleCodes[i]);

        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<BrowseProfileResponse>();
        while (await reader.ReadAsync())
        {
            list.Add(new BrowseProfileResponse
            {
                UserRoleId = reader.GetInt32(0),
                UserId     = reader.GetInt32(1),
                Name       = reader.GetString(2),
                Surname    = reader.GetString(3),
                Email      = reader.GetString(4),
                RoleCode   = reader.GetString(5),
                RoleName   = reader.GetString(6),
                BusinessName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Bio        = reader.IsDBNull(8) ? "" : reader.GetString(8),
                YearsExperience = reader.IsDBNull(9) ? 0 : Convert.ToInt64(reader.GetValue(9)),
                Phone      = reader.IsDBNull(10) ? "" : reader.GetString(10),
                IsSubscriptionActive = reader.GetBoolean(11),
                SubscriptionExpiryDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
            });
        }
        return list;
    }

    /// <summary>Resolves a user's userRoleId for a given role, matching against every known Afrikaans/English spelling alias of that role code or name.</summary>
    public virtual async Task<int> GetUserRoleId(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { roleCode };
        if (roleCode.Equals("PJ", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Uitruster", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Verskaffer", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("PJ");
            aliases.Add("Uitruster");
            aliases.Add("Verskaffer");
            aliases.Add("Toerusting Verskaffer");
        }
        else if (roleCode.Equals("Boer", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("FARMER", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Grondeienaar", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("Boer");
            aliases.Add("FARMER");
            aliases.Add("Farmer");
            aliases.Add("Grondeienaar");
        }
        else if (roleCode.Equals("PH", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Professionele Jagter", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Gids", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("PH");
            aliases.Add("Professionele Jagter");
            aliases.Add("Professional Hunter");
            aliases.Add("Gids");
        }
        else if (roleCode.Equals("VG", StringComparison.OrdinalIgnoreCase) || roleCode.Equals("Videograaf", StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add("VG");
            aliases.Add("Videograaf");
            aliases.Add("Videographer");
        }

        var paramNames = new List<string>();
        using var cmd = new SqlCommand();
        cmd.Connection = conn;
        cmd.Parameters.AddWithValue("@uid", userId);

        int idx = 0;
        foreach (var a in aliases)
        {
            string pName = $"@alias{idx++}";
            paramNames.Add(pName);
            cmd.Parameters.AddWithValue(pName, a);
        }

        string inClause = string.Join(", ", paramNames);
        cmd.CommandText = $@"SELECT TOP 1 ur.userRoleId 
                             FROM userRoles ur
                             JOIN roles r ON ur.urRoleId = r.roleId
                             WHERE ur.urUserId = @uid AND (r.roleCode IN ({inClause}) OR r.roleName IN ({inClause}))";

        var res = await cmd.ExecuteScalarAsync();
        return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
    }

    /// <summary>Resolves a user's first (lowest-id) userRoleId regardless of role - a fallback when a specific role match isn't required/found.</summary>
    public virtual async Task<int> GetAnyUserRoleId(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT TOP 1 ur.userRoleId 
                     FROM userRoles ur
                     WHERE ur.urUserId = @uid
                     ORDER BY ur.userRoleId ASC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        var res = await cmd.ExecuteScalarAsync();
        return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
    }

    /// <summary>Lists every PJ team a given member (PH/VG/Farmer userRoleId) belongs to.</summary>
    public virtual async Task<List<TeamMemberResponse>> GetTeamsForMember(int memberUserRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT t.teamId, t.teamPjId, u.userId, u.uName, u.uSurname, ro.roleCode
                     FROM team t
                     JOIN userRoles ur ON t.teamPjId = ur.userRoleId
                     JOIN users u ON ur.urUserId = u.userId
                     JOIN roles ro ON ur.urRoleId = ro.roleId
                     WHERE t.teamMemberId = @memberId";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@memberId", memberUserRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<TeamMemberResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new TeamMemberResponse
            {
                Id = r.GetInt32(0),
                MemberUserRoleId = r.GetInt32(1),
                UserId = r.GetInt32(2),
                Name = r.GetString(3),
                Surname = r.GetString(4),
                Role = r.GetString(5)
            });
        }
        return list;
    }

    /// <summary>Sends (or, if one already exists between this PJ and member, re-activates) a team invite, and best-effort notifies the invitee in-app.</summary>
    public virtual async Task<int> SendInvite(
        int pjUserRoleId,
        int memberUserRoleId,
        string? expeditionName = null,
        string? terms = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"IF EXISTS (SELECT 1 FROM teamInvites WHERE invitePjId = @pj AND inviteMemberId = @member)
                     BEGIN
                         UPDATE teamInvites SET inviteStatus = 'Pending', inviteDate = GETDATE(),
                             inviteExpeditionName = @expeditionName, inviteTerms = @terms
                         WHERE invitePjId = @pj AND inviteMemberId = @member;
                         SELECT inviteId FROM teamInvites WHERE invitePjId = @pj AND inviteMemberId = @member;
                     END
                     ELSE
                     BEGIN
                         INSERT INTO teamInvites (invitePjId, inviteMemberId, inviteStatus, inviteDate, inviteExpeditionName, inviteTerms)
                         VALUES (@pj, @member, 'Pending', GETDATE(), @expeditionName, @terms);
                         SELECT SCOPE_IDENTITY();
                     END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@pj",     pjUserRoleId);
        cmd.Parameters.AddWithValue("@member", memberUserRoleId);
        cmd.Parameters.AddWithValue("@expeditionName", string.IsNullOrWhiteSpace(expeditionName) ? (object)DBNull.Value : expeditionName.Trim());
        cmd.Parameters.AddWithValue("@terms", string.IsNullOrWhiteSpace(terms) ? (object)DBNull.Value : terms.Trim());
        var id = await cmd.ExecuteScalarAsync();
        int inviteId = Convert.ToInt32(id);

        try
        {
            string notifInfoQ = @"SELECT urPj.urUserId, ISNULL(uPj.uName + ' ' + uPj.uSurname, 'MikPunt Uitruster'), urM.urUserId
                                  FROM userRoles urPj
                                  JOIN users uPj ON urPj.urUserId = uPj.userId
                                  JOIN userRoles urM ON urM.userRoleId = @member
                                  WHERE urPj.userRoleId = @pj";
            using var notifInfoCmd = new SqlCommand(notifInfoQ, conn);
            notifInfoCmd.Parameters.AddWithValue("@pj", pjUserRoleId);
            notifInfoCmd.Parameters.AddWithValue("@member", memberUserRoleId);
            using var nr = await notifInfoCmd.ExecuteReaderAsync();
            if (await nr.ReadAsync())
            {
                int senderUid = nr.GetInt32(0);
                string pjName = nr.GetString(1);
                int recipientUid = nr.GetInt32(2);
                nr.Close();

                string notifInsertQ = @"INSERT INTO userNotifications (recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp)
                                       VALUES (@recipientUserId, @senderUserId, 'TEAM_INVITE', @title, @msg, '/uitnodigings', 0, GETDATE())";
                using var insCmd = new SqlCommand(notifInsertQ, conn);
                insCmd.Parameters.AddWithValue("@recipientUserId", recipientUid);
                insCmd.Parameters.AddWithValue("@senderUserId", senderUid);
                insCmd.Parameters.AddWithValue("@title", "Nuwe Spanuitnodiging");
                insCmd.Parameters.AddWithValue("@msg", $"{pjName} het u uitgenooi om by hul span aan te sluit.");
                await insCmd.ExecuteNonQueryAsync();
            }
        }
        catch { }

        return inviteId;
    }

    /// <summary>Stores the expedition name and offered terms the PJ typed on the invite (shown to the invitee).</summary>
    public virtual async Task SaveInviteDetails(int inviteId, string? expeditionName, string? terms)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("UPDATE teamInvites SET inviteExpeditionName = @e, inviteTerms = @t WHERE inviteId = @id", conn);
        cmd.Parameters.AddWithValue("@id", inviteId);
        cmd.Parameters.AddWithValue("@e", string.IsNullOrWhiteSpace(expeditionName) ? DBNull.Value : expeditionName.Trim());
        cmd.Parameters.AddWithValue("@t", string.IsNullOrWhiteSpace(terms) ? DBNull.Value : terms.Trim());
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Lists pending invites addressed to a specific member userRoleId.</summary>
    public virtual async Task<List<TeamInviteResponse>> GetInvitesForMember(int memberUserRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT ti.inviteId, ti.invitePjId, ur.urUserId, u.uName, u.uSurname, u.uEmail, ti.inviteStatus, ti.inviteDate, ti.inviteExpeditionName, ti.inviteTerms
                     FROM teamInvites ti
                     JOIN userRoles ur ON ti.invitePjId = ur.userRoleId
                     JOIN users u ON ur.urUserId = u.userId
                     WHERE ti.inviteMemberId = @memberId AND ti.inviteStatus = 'Pending'
                     ORDER BY ti.inviteDate DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@memberId", memberUserRoleId);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TeamInviteResponse>();
        while (await reader.ReadAsync())
        {
            list.Add(new TeamInviteResponse
            {
                InviteId     = reader.GetInt32(0),
                PjUserRoleId = reader.GetInt32(1),
                PjUserId     = reader.GetInt32(2),
                PjName       = reader.GetString(3),
                PjSurname    = reader.GetString(4),
                PjEmail      = reader.GetString(5),
                Status       = reader.GetString(6),
                InviteDate   = reader.GetDateTime(7),
                ExpeditionName = reader.IsDBNull(8) ? null : reader.GetString(8),
                Terms        = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        return list;
    }

    /// <summary>Lists every invite (any status) addressed to a user across all of their roles, pending ones first.</summary>
    public virtual Task<List<TeamInviteResponse>> GetInvitesForUser(int userId)
        => QueryInvitesAsync("WHERE urM.urUserId = @userId ORDER BY CASE WHEN ti.inviteStatus = 'Pending' THEN 0 ELSE 1 END, ti.inviteDate DESC", userId);

    /// <summary>Lists every invite a PJ has sent, any status, for their own "sent invites" tracking view.</summary>
    public virtual Task<List<TeamInviteResponse>> GetSentInvitesForPj(int pjUserId)
        => QueryInvitesAsync("WHERE urPj.urUserId = @userId ORDER BY ti.inviteDate DESC", pjUserId);

    /// <summary>Invites with both sides' names, filtered/ordered by <paramref name="tail"/> (on @userId).</summary>
    private async Task<List<TeamInviteResponse>> QueryInvitesAsync(string tail, int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT ti.inviteId, ti.invitePjId, urPj.urUserId, uPj.uName, uPj.uSurname, uPj.uEmail,
                            uM.userId as targetUserId, uM.uName + ' ' + uM.uSurname as targetName, rM.roleCode as targetRoleCode,
                            ti.inviteStatus, ti.inviteDate, ti.inviteExpeditionName, ti.inviteTerms
                     FROM teamInvites ti
                     JOIN userRoles urPj ON ti.invitePjId = urPj.userRoleId
                     JOIN users uPj ON urPj.urUserId = uPj.userId
                     JOIN userRoles urM ON ti.inviteMemberId = urM.userRoleId
                     JOIN users uM ON urM.urUserId = uM.userId
                     JOIN roles rM ON urM.urRoleId = rM.roleId
                     " + tail;
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TeamInviteResponse>();
        while (await reader.ReadAsync())
        {
            list.Add(new TeamInviteResponse
            {
                InviteId       = reader.GetInt32(0),
                PjUserRoleId   = reader.GetInt32(1),
                PjUserId       = reader.GetInt32(2),
                PjName         = reader.GetString(3),
                PjSurname      = reader.GetString(4),
                PjEmail        = reader.GetString(5),
                TargetUserId   = reader.GetInt32(6),
                TargetName     = reader.GetString(7),
                TargetRoleCode = reader.GetString(8),
                Status         = reader.GetString(9),
                InviteDate     = reader.GetDateTime(10),
                ExpeditionName = reader.IsDBNull(11) ? null : reader.GetString(11),
                Terms          = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        return list;
    }

    /// <summary>Withdraws a still-pending invite (PJ-initiated cancellation before the target responds).</summary>
    public virtual async Task<bool> CancelInvite(int inviteId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "DELETE FROM teamInvites WHERE inviteId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", inviteId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Accepts or declines an invite; on accept, creates the team-membership row if one doesn't already exist, and notifies the PJ either way. All in one transaction.</summary>
    public virtual async Task<bool> RespondToInvite(int inviteId, bool accept)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            string newStatus = accept ? "Accepted" : "Rejected";
            string updateQ = "UPDATE teamInvites SET inviteStatus = @status WHERE inviteId = @id";
            using (var cmd = new SqlCommand(updateQ, conn, tx))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@id", inviteId);
                int rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    tx.Rollback();
                    return false;
                }
            }

            string selectQ = "SELECT invitePjId, inviteMemberId FROM teamInvites WHERE inviteId = @id";
            int pjId = 0, memberId = 0;
            using (var selCmd = new SqlCommand(selectQ, conn, tx))
            {
                selCmd.Parameters.AddWithValue("@id", inviteId);
                using var r = await selCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    pjId     = r.GetInt32(0);
                    memberId = r.GetInt32(1);
                }
            }

            if (accept && pjId > 0 && memberId > 0)
            {
                string checkQ = "SELECT COUNT(*) FROM team WHERE teamPjId = @pj AND teamMemberId = @member";
                using (var checkCmd = new SqlCommand(checkQ, conn, tx))
                {
                    checkCmd.Parameters.AddWithValue("@pj", pjId);
                    checkCmd.Parameters.AddWithValue("@member", memberId);
                    int existing = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (existing == 0)
                    {
                        string insertQ = "INSERT INTO team (teamPjId, teamMemberId) VALUES (@pj, @member)";
                        using var insCmd = new SqlCommand(insertQ, conn, tx);
                        insCmd.Parameters.AddWithValue("@pj", pjId);
                        insCmd.Parameters.AddWithValue("@member", memberId);
                        await insCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // Insert notification for the PJ
            if (pjId > 0 && memberId > 0)
            {
                string infoQ = @"SELECT urPj.urUserId, urM.urUserId, ISNULL(uM.uName + ' ' + uM.uSurname, 'Spanlid')
                                 FROM userRoles urPj
                                 CROSS JOIN userRoles urM
                                 JOIN users uM ON urM.urUserId = uM.userId
                                 WHERE urPj.userRoleId = @pjId AND urM.userRoleId = @memberId";
                using var infoCmd = new SqlCommand(infoQ, conn, tx);
                infoCmd.Parameters.AddWithValue("@pjId", pjId);
                infoCmd.Parameters.AddWithValue("@memberId", memberId);
                using var ir = await infoCmd.ExecuteReaderAsync();
                if (await ir.ReadAsync())
                {
                    int pjUid = ir.GetInt32(0);
                    int memberUid = ir.GetInt32(1);
                    string memberName = ir.GetString(2);
                    ir.Close();

                    string title = accept ? "Spanuitnodiging Aanvaar" : "Spanuitnodiging Van die Hand Gewys";
                    string msg = accept ? $"{memberName} het u uitnodiging aanvaar om by die span aan te sluit." : $"{memberName} het u spanuitnodiging van die hand gewys.";
                    string notifInsertQ = @"INSERT INTO userNotifications (recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp)
                                           VALUES (@recipientUserId, @senderUserId, 'INVITE_RESPONSE', @title, @msg, '/pj/span', 0, GETDATE())";
                    using var insNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                    insNotifCmd.Parameters.AddWithValue("@recipientUserId", pjUid);
                    insNotifCmd.Parameters.AddWithValue("@senderUserId", memberUid);
                    insNotifCmd.Parameters.AddWithValue("@title", title);
                    insNotifCmd.Parameters.AddWithValue("@msg", msg);
                    await insNotifCmd.ExecuteNonQueryAsync();
                }
            }

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Checks whether removing a team member would conflict with bookings the
    /// member is tied to (as PH/VG/farm) - an active/in-progress booking blocks
    /// removal outright, while upcoming-but-not-yet-active bookings are surfaced
    /// for the PJ to confirm before proceeding. Called before ExecuteTeamMemberRemoval
    /// so the caller can decide/confirm rather than being surprised by a rejection.
    /// </summary>
    public virtual async Task<RemovalConflictCheckResponse> CheckMemberRemovalConflicts(int teamId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string teamQuery = @"
            SELECT t.teamId,
                   ISNULL(urPj.urUserId, 0) AS pjUserId,
                   t.teamPjId AS pjUserRoleId,
                   ISNULL(pj1.pjId, 0) AS pjId,
                   t.teamMemberId,
                   mUser.userId AS memberUserId,
                   ISNULL(mUser.uName + ' ' + mUser.uSurname, '') AS memberName,
                   mRole.roleCode AS memberRoleCode
            FROM team t
            LEFT JOIN userRoles urPj ON t.teamPjId = urPj.userRoleId
            LEFT JOIN pj pj1 ON pj1.pjUserRoleId = urPj.userRoleId
            JOIN userRoles urMember ON t.teamMemberId = urMember.userRoleId
            JOIN users mUser ON urMember.urUserId = mUser.userId
            JOIN roles mRole ON urMember.urRoleId = mRole.roleId
            WHERE t.teamId = @teamId";

        int pjUserId = 0;
        int pjUserRoleId = 0;
        int pjId = 0;
        int memberUserRoleId = 0;
        int memberUserId = 0;
        string memberName = "";
        string memberRoleCode = "";

        using (var cmd = new SqlCommand(teamQuery, conn))
        {
            cmd.Parameters.AddWithValue("@teamId", teamId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                pjUserId = reader.GetInt32(1);
                pjUserRoleId = reader.GetInt32(2);
                pjId = reader.GetInt32(3);
                memberUserRoleId = reader.GetInt32(4);
                memberUserId = reader.GetInt32(5);
                memberName = reader.GetString(6);
                memberRoleCode = reader.GetString(7);
            }
            else
            {
                return new RemovalConflictCheckResponse
                {
                    TeamId = teamId,
                    CanRemoveDirectly = false,
                    Message = "Spanlid nie gevind nie."
                };
            }
        }

        string bookingsQuery = @"
            SELECT b.bookingId,
                   ISNULL(p.pTitle, '') AS packageTitle,
                   b.bookStartDate,
                   b.bookEndDate,
                   ISNULL(hUser.uName + ' ' + hUser.uSurname, 'Onbekend') AS hunterName,
                   ISNULL(b.bookStatus, '') AS bookStatus
            FROM bookings b
            JOIN packages p ON b.bookPackageId = p.packageId
            LEFT JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
            LEFT JOIN users hUser ON urH.urUserId = hUser.userId
            LEFT JOIN ph ON b.bookPhId = ph.phId
            LEFT JOIN vg ON b.bookVgId = vg.vgId
            LEFT JOIN farm f ON p.pFarmId = f.farmId
            LEFT JOIN farmer fa ON f.farmFarmerId = fa.farmerId
            WHERE (p.pPjId = @pjId 
                   OR p.pPjId IN (SELECT pj.pjId FROM pj WHERE pj.pjUserRoleId = @pjUserRoleId))
              AND (
                   ph.phUserRoleId = @memberUserRoleId
                OR vg.vgUserRoleId = @memberUserRoleId
                OR fa.fUserRoleId = @memberUserRoleId
              )
              AND b.bookStatus NOT IN ('Cancelled', 'Gekanselleer', 'Rejected', 'Voltooi', 'Completed')";

        var activeBookings = new List<BookingConflictDto>();
        var upcomingBookings = new List<BookingConflictDto>();
        var today = DateTime.UtcNow.Date;

        var activeStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Approved", "Goedgekeur", "In Progress", "In Uitvoering", "Active", "Aktief"
        };
        var upcomingStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Approved", "Goedgekeur"
        };

        using (var cmd = new SqlCommand(bookingsQuery, conn))
        {
            cmd.Parameters.AddWithValue("@pjId", pjId);
            cmd.Parameters.AddWithValue("@pjUserRoleId", pjUserRoleId);
            cmd.Parameters.AddWithValue("@memberUserRoleId", memberUserRoleId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int bId = reader.GetInt32(0);
                string title = reader.GetString(1);
                DateTime start = reader.GetDateTime(2);
                DateTime end = reader.GetDateTime(3);
                string hunter = reader.GetString(4);
                string status = reader.GetString(5);

                var conflictDto = new BookingConflictDto
                {
                    BookingId = bId,
                    PackageTitle = title,
                    StartDate = start,
                    EndDate = end,
                    HunterName = hunter,
                    Status = status
                };

                if (activeStatuses.Contains(status) && today >= start.Date && today <= end.Date)
                {
                    activeBookings.Add(conflictDto);
                }
                else if (upcomingStatuses.Contains(status) && start.Date > today)
                {
                    upcomingBookings.Add(conflictDto);
                }
            }
        }

        bool hasActiveConflict = activeBookings.Count > 0;
        bool hasUpcomingConflict = upcomingBookings.Count > 0;
        bool canRemoveDirectly = !hasActiveConflict && !hasUpcomingConflict;

        string message;
        if (hasActiveConflict)
        {
            message = $"Aktiewe jag-konflik: {memberName} is tans gekoppel aan {activeBookings.Count} aktiewe bespreking(s). Lid kan nie tydens 'n aktiewe jag verwyder word nie.";
        }
        else if (hasUpcomingConflict)
        {
            message = $"Toekomstige besprekings: {memberName} is gekoppel aan {upcomingBookings.Count} toekomstige bespreking(s). Bevestiging van hertoewysing word vereis.";
        }
        else
        {
            message = "Lid het geen botsende besprekings nie en kan direk verwyder word.";
        }

        return new RemovalConflictCheckResponse
        {
            TeamId = teamId,
            MemberUserId = memberUserId,
            MemberName = memberName,
            MemberRole = memberRoleCode,
            CanRemoveDirectly = canRemoveDirectly,
            HasActiveBookingConflict = hasActiveConflict,
            HasUpcomingBookingConflict = hasUpcomingConflict,
            ActiveBookings = activeBookings,
            UpcomingBookings = upcomingBookings,
            Message = message
        };
    }

    /// <summary>Standard reason codes a PJ picks from when removing a team member - static list, no DB access.</summary>
    public virtual List<RemovalReasonOptionDto> GetRemovalReasons()
    {
        return new List<RemovalReasonOptionDto>
        {
            new() { Code = "MUTUAL_AGREEMENT", Description = "Onderlinge ooreenkoms / Mutual agreement" },
            new() { Code = "CONTRACT_EXPIRED", Description = "Kontrak het verstryk / Contract expired" },
            new() { Code = "UNAVAILABLE", Description = "Nie meer beskikbaar nie / No longer available" },
            new() { Code = "PERFORMANCE_ISSUES", Description = "Prestasiekwessies / Performance issues" },
            new() { Code = "CONDUCT_VIOLATION", Description = "Gedragskode oortreding / Code of conduct violation" },
            new() { Code = "MEMBER_REQUEST", Description = "Lid het versoek om span te verlaat / Member requested departure" },
            new() { Code = "RESTRUCTURING", Description = "Span herstrukturering / Team restructuring" },
            new() { Code = "OTHER", Description = "Ander rede / Other reason" }
        };
    }

    /// <summary>
    /// Removes a team member: validates the caller actually owns this team,
    /// re-checks booking conflicts (an active one throws
    /// InvalidOperationException("ACTIVE_BOOKING_CONFLICT"); upcoming ones require
    /// request.ConfirmedDespiteUpcomingBookings or throw
    /// InvalidOperationException("UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION") - both
    /// caught and translated to specific HTTP responses by
    /// TeamController.RemoveMember), deletes the team row, writes a teamAudit entry,
    /// and returns enough detail (name/email/unassigned-booking count) for the
    /// caller to notify the removed member. All in one transaction.
    /// </summary>
    public virtual async Task<TeamRemovalResult> ExecuteTeamMemberRemoval(int teamId, RemoveTeamMemberRequest request, int pjUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            // 1. Validates teamId exists and PJ is owner.
            string teamQuery = @"
                SELECT t.teamId,
                       ISNULL(urPj.urUserId, 0) AS pjUserId,
                       t.teamPjId AS pjUserRoleId,
                       ISNULL(pj1.pjId, 0) AS pjId,
                       t.teamMemberId,
                       mUser.userId AS memberUserId,
                       ISNULL(mUser.uName + ' ' + mUser.uSurname, '') AS memberName,
                       mUser.uEmail AS memberEmail,
                       mRole.roleCode AS memberRoleCode
                FROM team t
                LEFT JOIN userRoles urPj ON t.teamPjId = urPj.userRoleId
                LEFT JOIN pj pj1 ON pj1.pjUserRoleId = urPj.userRoleId
                JOIN userRoles urMember ON t.teamMemberId = urMember.userRoleId
                JOIN users mUser ON urMember.urUserId = mUser.userId
                JOIN roles mRole ON urMember.urRoleId = mRole.roleId
                WHERE t.teamId = @teamId";

            int dbPjUserId = 0;
            int pjUserRoleId = 0;
            int pjId = 0;
            int memberUserRoleId = 0;
            int memberUserId = 0;
            string memberName = "";
            string memberEmail = "";
            string memberRoleCode = "";

            using (var cmd = new SqlCommand(teamQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@teamId", teamId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    dbPjUserId = reader.GetInt32(1);
                    pjUserRoleId = reader.GetInt32(2);
                    pjId = reader.GetInt32(3);
                    memberUserRoleId = reader.GetInt32(4);
                    memberUserId = reader.GetInt32(5);
                    memberName = reader.GetString(6);
                    memberEmail = reader.IsDBNull(7) ? "" : reader.GetString(7);
                    memberRoleCode = reader.GetString(8);
                }
                else
                {
                    throw new KeyNotFoundException("Spanlid nie gevind nie.");
                }
            }

            // pjUserId <= 0 means an Admin caller (TeamController passes 0 for Admins).
            if (pjUserId > 0 && pjUserId != dbPjUserId)
            {
                throw new UnauthorizedAccessException("U is nie die eienaar van hierdie span nie.");
            }

            // 2. Re-checks active booking conflicts and upcoming bookings
            string bookingsQuery = @"
                SELECT b.bookingId,
                       b.bookStartDate,
                       b.bookEndDate,
                       ISNULL(b.bookStatus, '') AS bookStatus
                FROM bookings b
                JOIN packages p ON b.bookPackageId = p.packageId
                LEFT JOIN ph ON b.bookPhId = ph.phId
                LEFT JOIN vg ON b.bookVgId = vg.vgId
                LEFT JOIN farm f ON p.pFarmId = f.farmId
                LEFT JOIN farmer fa ON f.farmFarmerId = fa.farmerId
                WHERE (p.pPjId = @pjId 
                       OR p.pPjId IN (SELECT pj.pjId FROM pj WHERE pj.pjUserRoleId = @pjUserRoleId))
                  AND (
                       ph.phUserRoleId = @memberUserRoleId
                    OR vg.vgUserRoleId = @memberUserRoleId
                    OR fa.fUserRoleId = @memberUserRoleId
                  )
                  AND b.bookStatus NOT IN ('Cancelled', 'Gekanselleer', 'Rejected', 'Voltooi', 'Completed')";

            var today = DateTime.UtcNow.Date;
            var activeBookingIds = new List<int>();
            var upcomingBookingIds = new List<int>();

            var activeStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Approved", "Goedgekeur", "In Progress", "In Uitvoering", "Active", "Aktief"
            };
            var upcomingStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Approved", "Goedgekeur"
            };

            using (var cmd = new SqlCommand(bookingsQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@pjId", pjId);
                cmd.Parameters.AddWithValue("@pjUserRoleId", pjUserRoleId);
                cmd.Parameters.AddWithValue("@memberUserRoleId", memberUserRoleId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int bId = reader.GetInt32(0);
                    DateTime start = reader.GetDateTime(1);
                    DateTime end = reader.GetDateTime(2);
                    string status = reader.GetString(3);

                    if (activeStatuses.Contains(status) && today >= start.Date && today <= end.Date)
                    {
                        activeBookingIds.Add(bId);
                    }
                    else if (upcomingStatuses.Contains(status) && start.Date > today)
                    {
                        upcomingBookingIds.Add(bId);
                    }
                }
            }

            // 2. If active booking exists, throw InvalidOperationException("ACTIVE_BOOKING_CONFLICT").
            if (activeBookingIds.Count > 0)
            {
                throw new InvalidOperationException("ACTIVE_BOOKING_CONFLICT");
            }

            // 3. If upcoming bookings exist and !request.ConfirmReassignment, throw InvalidOperationException("UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION").
            if (upcomingBookingIds.Count > 0 && !request.ConfirmReassignment)
            {
                throw new InvalidOperationException("UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION");
            }

            // 4. If upcoming bookings exist and confirmed: unassign member
            var unassignedBookingIds = new List<int>();
            if (upcomingBookingIds.Count > 0 && request.ConfirmReassignment)
            {
                var paramNames = upcomingBookingIds.Select((_, idx) => $"@bid{idx}").ToList();
                string unassignQuery = $@"
                    UPDATE b
                    SET b.bookPhId = CASE WHEN ph.phUserRoleId = @memberUserRoleId OR b.bookPhId = @memberUserRoleId THEN NULL ELSE b.bookPhId END,
                        b.bookVgId = CASE WHEN vg.vgUserRoleId = @memberUserRoleId OR b.bookVgId = @memberUserRoleId THEN NULL ELSE b.bookVgId END
                    FROM bookings b
                    LEFT JOIN ph ON b.bookPhId = ph.phId
                    LEFT JOIN vg ON b.bookVgId = vg.vgId
                    WHERE b.bookingId IN ({string.Join(",", paramNames)})";

                using (var cmd = new SqlCommand(unassignQuery, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@memberUserRoleId", memberUserRoleId);
                    for (int i = 0; i < upcomingBookingIds.Count; i++)
                    {
                        cmd.Parameters.AddWithValue(paramNames[i], upcomingBookingIds[i]);
                    }
                    await cmd.ExecuteNonQueryAsync();
                }
                unassignedBookingIds.AddRange(upcomingBookingIds);
            }

            // 5. Insert record into teamAudit.
            string auditQuery = @"
                INSERT INTO teamAudit (
                    teamId, pjUserId, pjUserRoleId, memberUserId, memberUserRoleId,
                    memberRoleCode, actionType, reasonCode, reasonDescription, notes,
                    hadUpcomingBookings, affectedBookingIds, timestamp
                ) VALUES (
                    @teamId, @pjUserId, @pjUserRoleId, @memberUserId, @memberUserRoleId,
                    @memberRoleCode, 'REMOVED', @reasonCode, @reasonDescription, @notes,
                    @hadUpcomingBookings, @affectedBookingIds, GETDATE()
                )";

            using (var cmd = new SqlCommand(auditQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@teamId", teamId);
                cmd.Parameters.AddWithValue("@pjUserId", pjUserId > 0 ? pjUserId : dbPjUserId);
                cmd.Parameters.AddWithValue("@pjUserRoleId", pjUserRoleId);
                cmd.Parameters.AddWithValue("@memberUserId", memberUserId);
                cmd.Parameters.AddWithValue("@memberUserRoleId", memberUserRoleId);
                cmd.Parameters.AddWithValue("@memberRoleCode", (object?)memberRoleCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reasonCode", (object?)request.ReasonCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reasonDescription", (object?)request.ReasonDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", (object?)request.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@hadUpcomingBookings", unassignedBookingIds.Count > 0);
                cmd.Parameters.AddWithValue("@affectedBookingIds", unassignedBookingIds.Count > 0 ? string.Join(",", unassignedBookingIds) : (object)DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }

            // 6. DELETE FROM team WHERE teamId = @teamId.
            string deleteTeamQuery = "DELETE FROM team WHERE teamId = @teamId";
            using (var cmd = new SqlCommand(deleteTeamQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@teamId", teamId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 7. UPDATE teamInvites SET inviteStatus = 'Removed' WHERE invitePjId = @pjRoleId AND inviteMemberId = @mRoleId.
            string updateInviteQuery = @"
                UPDATE teamInvites 
                SET inviteStatus = 'Removed' 
                WHERE invitePjId = @pjRoleId AND inviteMemberId = @memberRoleId";
            using (var cmd = new SqlCommand(updateInviteQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@pjRoleId", pjUserRoleId);
                cmd.Parameters.AddWithValue("@memberRoleId", memberUserRoleId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 8. Update member visibility (phPubliclyVisibleTill, vgPubliclyVisibleTill, fPubliclyVisibleTill).
            string visibilityQuery = @"
                UPDATE ph SET phPubliclyVisibleTill = DATEADD(year, 1, GETDATE()) WHERE phUserRoleId = @memberRoleId;
                UPDATE vg SET vgPubliclyVisibleTill = DATEADD(year, 1, GETDATE()) WHERE vgUserRoleId = @memberRoleId;
                UPDATE farmer SET fPubliclyVisibleTill = DATEADD(year, 1, GETDATE()) WHERE fUserRoleId = @memberRoleId;";
            using (var cmd = new SqlCommand(visibilityQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@memberRoleId", memberUserRoleId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 9. Insert in-app notification into userNotifications.
            string notificationQuery = @"
                INSERT INTO userNotifications (
                    recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp
                ) VALUES (
                    @recipientUserId, @senderUserId, @notificationType, @title, @message, @actionUrl, 0, GETDATE()
                )";
            using (var cmd = new SqlCommand(notificationQuery, conn, tx))
            {
                cmd.Parameters.AddWithValue("@recipientUserId", memberUserId);
                cmd.Parameters.AddWithValue("@senderUserId", pjUserId > 0 ? pjUserId : dbPjUserId);
                cmd.Parameters.AddWithValue("@notificationType", "TEAM_REMOVAL");
                cmd.Parameters.AddWithValue("@title", "Spanlidmaatskap Beëindig / Team Membership Terminated");
                string descPart = !string.IsNullOrWhiteSpace(request.ReasonDescription) 
                    ? request.ReasonDescription 
                    : (!string.IsNullOrWhiteSpace(request.ReasonCode) ? request.ReasonCode : "Onderlinge ooreenkoms");
                string notePart = !string.IsNullOrWhiteSpace(request.Notes) ? $" Notas: {request.Notes}" : "";
                cmd.Parameters.AddWithValue("@message", $"U is verwyder uit die span deur die uitruster. Rede: {descPart}.{notePart}");
                cmd.Parameters.AddWithValue("@actionUrl", "/profiel");
                await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();

            return new TeamRemovalResult
            {
                Success = true,
                TeamId = teamId,
                RemovedMemberUserId = memberUserId,
                RemovedMemberName = memberName,
                RemovedMemberEmail = memberEmail,
                UnassignedBookingCount = unassignedBookingIds.Count,
                UnassignedBookingIds = unassignedBookingIds,
                Message = $"Lid {memberName} is suksesvol uit die span verwyder."
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public virtual async Task<int> GetUnreadNotificationCount(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT COUNT(*) FROM userNotifications WHERE recipientUserId = @userId AND isRead = 0";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        var res = await cmd.ExecuteScalarAsync();
        return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
    }

    public virtual async Task<List<UserNotificationDto>> GetUserNotifications(int userId, int page = 1, int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        int offset = (page - 1) * pageSize;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT notificationId, recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp
                     FROM userNotifications
                     WHERE recipientUserId = @userId
                     ORDER BY createdTimestamp DESC
                     OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@offset", offset);
        cmd.Parameters.AddWithValue("@pageSize", pageSize);

        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UserNotificationDto>();
        while (await reader.ReadAsync())
        {
            list.Add(new UserNotificationDto
            {
                NotificationId = reader.GetInt32(0),
                RecipientUserId = reader.GetInt32(1),
                SenderUserId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                NotificationType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Title = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Message = reader.IsDBNull(5) ? "" : reader.GetString(5),
                ActionUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsRead = reader.GetBoolean(7),
                CreatedTimestamp = reader.GetDateTime(8)
            });
        }
        return list;
    }

    /// <summary>
    /// Ownership is enforced in the WHERE clause itself, not by a separate lookup -
    /// a notification belonging to someone else (and caller isn't Admin) matches zero
    /// rows and returns false, the same "not found" contract as a nonexistent id.
    /// </summary>
    public virtual async Task<bool> MarkNotificationAsRead(int notificationId, int callerUserId, bool isAdmin)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"UPDATE userNotifications SET isRead = 1, readTimestamp = GETDATE()
                     WHERE notificationId = @id AND (recipientUserId = @callerUserId OR @isAdmin = 1)";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", notificationId);
        cmd.Parameters.AddWithValue("@callerUserId", callerUserId);
        cmd.Parameters.AddWithValue("@isAdmin", isAdmin);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public virtual async Task<bool> InsertNotification(int recipientUserId, int? senderUserId, string type, string title, string message, string? actionUrl = null)
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            string q = @"INSERT INTO userNotifications (
                            recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp
                         ) VALUES (
                            @recipientUserId, @senderUserId, @type, @title, @message, @actionUrl, 0, GETDATE()
                         )";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@recipientUserId", recipientUserId);
            cmd.Parameters.AddWithValue("@senderUserId", senderUserId.HasValue ? (object)senderUserId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@type", type ?? "INFO");
            cmd.Parameters.AddWithValue("@title", title ?? "");
            cmd.Parameters.AddWithValue("@message", message ?? "");
            cmd.Parameters.AddWithValue("@actionUrl", actionUrl ?? (object)DBNull.Value);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch
        {
            return false;
        }
    }
}

