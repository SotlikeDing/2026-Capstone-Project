using Microsoft.Data.SqlClient;
using WebAPI.Services.Ai;

namespace WebAPI.Database;

/// <summary>One PJ / PH / VG / Boer role holder, with everything Smart Teaming scores on. RoleCode is canonical: PJ, PH, VG or FARMER.</summary>
public sealed class TeamProfile
{
    public int UserRoleId { get; init; }
    public int UserId { get; init; }
    public string RoleCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string BusinessName { get; init; } = "";
    public long YearsExperience { get; init; }
    /// <summary>pj.pjId / ph.phId / vg.vgId / farmer.farmerId - what bookings, packages and farms reference.</summary>
    public int? EntityId { get; init; }
    /// <summary>Email verified and not suspended - only visible profiles are ever suggested.</summary>
    public bool Visible { get; init; }
    /// <summary>Canonical province ids (see SpeciesCatalog.Provinces) the person works in.</summary>
    public HashSet<string> Provinces { get; } = new();
    public double? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    /// <summary>PH: lowest day rate. VG: average package day rate. FARMER: average lodge day rate. PJ: average package price. As listed.</summary>
    public decimal? DailyRate { get; set; }
    public int CompletedHunts { get; set; }
}

/// <summary>Everyone who could be teamed up, plus every relationship that already exists between them.</summary>
public sealed class TeamGraph
{
    public Dictionary<int, TeamProfile> ByUserRoleId { get; } = new();
    /// <summary>(PJ userRoleId, member userRoleId) -> bookings they worked together (approved, paid or completed).</summary>
    public Dictionary<(int PjUserRoleId, int MemberUserRoleId), int> JointHunts { get; } = new();
    /// <summary>(PJ userRoleId, member userRoleId) pairs that are already a team.</summary>
    public HashSet<(int PjUserRoleId, int MemberUserRoleId)> TeamRows { get; } = new();
    /// <summary>(PJ userRoleId, member userRoleId) -> latest invite status (Pending / Accepted / Declined / ...).</summary>
    public Dictionary<(int PjUserRoleId, int MemberUserRoleId), string> Invites { get; } = new();
}

public interface ITeamMatchStore
{
    Task<TeamGraph> LoadGraphAsync();
}

/// <summary>
/// Reads the teaming graph in one pass (the platform is small). Every query after the profile list is best-effort: a missing
/// table or column means fewer signals for the score, never a failed request.
/// </summary>
public class AiTeam_db : ITeamMatchStore
{
    private readonly string _connectionString;

    public AiTeam_db(string connectionString) => _connectionString = connectionString;

    private const string Worked = "('Approved', 'Paid', 'Completed')";

    public async Task<TeamGraph> LoadGraphAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        var g = new TeamGraph();

        const string profilesQ = @"
            SELECT ur.userRoleId, u.userId, ISNULL(u.uName, ''), ISNULL(u.uSurname, ''), ISNULL(r.roleCode, ''),
                   COALESCE(pj.pjBusinessName, f.fBusinessName, vg.vgCompanyName, ''),
                   CAST(COALESCE(pj.pjYearsOfExperience, ph.phYearsOfExperience, vg.vgYearsOfExperience, 0) AS BIGINT),
                   pj.pjId, ph.phId, vg.vgId, f.farmerId,
                   CAST(CASE WHEN ISNULL(u.uIsEmailVerified, 0) = 1 AND ISNULL(u.uIsSuspended, 0) = 0 THEN 1 ELSE 0 END AS BIT)
            FROM userRoles ur
            JOIN users u ON ur.urUserId = u.userId
            JOIN roles r ON ur.urRoleId = r.roleId
            LEFT JOIN pj ON pj.pjUserRoleId = ur.userRoleId
            LEFT JOIN ph ON ph.phUserRoleId = ur.userRoleId
            LEFT JOIN vg ON vg.vgUserRoleId = ur.userRoleId
            LEFT JOIN farmer f ON f.fUserRoleId = ur.userRoleId";
        using (var cmd = new SqlCommand(profilesQ, conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var role = PackageAdvisorService.NormalizeRole(r.GetString(4));
                if (role is null or "AP") continue;
                int urId = r.GetInt32(0);
                if (g.ByUserRoleId.ContainsKey(urId)) continue;       // duplicate role-detail rows: first wins
                int? entity = role switch
                {
                    "PJ" => r.IsDBNull(7) ? null : r.GetInt32(7),
                    "PH" => r.IsDBNull(8) ? null : r.GetInt32(8),
                    "VG" => r.IsDBNull(9) ? null : r.GetInt32(9),
                    _ => r.IsDBNull(10) ? null : r.GetInt32(10)
                };
                g.ByUserRoleId[urId] = new TeamProfile
                {
                    UserRoleId = urId, UserId = r.GetInt32(1),
                    Name = $"{r.GetString(2)} {r.GetString(3)}".Trim(), RoleCode = role,
                    BusinessName = r.IsDBNull(5) ? "" : r.GetString(5), YearsExperience = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                    EntityId = entity, Visible = r.GetBoolean(11)
                };
            }
        }

        var byEntity = g.ByUserRoleId.Values.Where(p => p.EntityId.HasValue)
            .GroupBy(p => (p.RoleCode, p.EntityId!.Value)).ToDictionary(x => x.Key, x => x.First());
        TeamProfile? Entity(string role, int id) => byEntity.TryGetValue((role, id), out var p) ? p : null;

        // ---- provinces
        async Task Regions(string role, string sql) => await TryReadAsync(conn, sql, r =>
        {
            if (r.IsDBNull(0) || r.IsDBNull(1)) return;
            var prov = SpeciesCatalog.TryNormalizeProvince(r.GetString(1));
            if (prov != null) Entity(role, r.GetInt32(0))?.Provinces.Add(prov);
        });
        await Regions("FARMER", "SELECT f.farmFarmerId, a.addressRegion FROM farm f JOIN addresses a ON f.farmAddressId = a.addressId");
        await Regions("PJ", @"SELECT p.pPjId, a.addressRegion FROM packages p JOIN farm f ON p.pFarmId = f.farmId
                              JOIN addresses a ON f.farmAddressId = a.addressId");
        await Regions("VG", "SELECT v.vgapVgId, pr.provName FROM vgAvailableProvinces v JOIN provinces pr ON v.vgapProvId = pr.provId");
        await Regions("PH", @"SELECT b.bookPhId, a.addressRegion FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                              JOIN farm f ON p.pFarmId = f.farmId JOIN addresses a ON f.farmAddressId = a.addressId");

        // ---- ratings (moderated-out ratings excluded, same as the public profile average)
        await TryReadAsync(conn, @"SELECT rateeUserId, rateeRoleCode, COUNT(*), AVG(CAST(stars AS FLOAT))
                                   FROM ratings WHERE ISNULL(isExcluded, 0) = 0 GROUP BY rateeUserId, rateeRoleCode", r =>
        {
            var role = PackageAdvisorService.NormalizeRole(r.GetString(1));
            foreach (var p in g.ByUserRoleId.Values.Where(p => p.UserId == r.GetInt32(0) && p.RoleCode == role))
            {
                p.RatingCount = r.GetInt32(2);
                p.RatingAverage = Math.Round(r.GetDouble(3), 2);
            }
        });

        // ---- rates
        async Task Rates(string role, string sql) => await TryReadAsync(conn, sql, r =>
        {
            if (!r.IsDBNull(0) && !r.IsDBNull(1) && Entity(role, r.GetInt32(0)) is { } p) p.DailyRate = Math.Round(r.GetDecimal(1), 0);
        });
        await Rates("PH", "SELECT phpPhId, MIN(phpPrice) FROM phPricing WHERE phpPrice > 0 GROUP BY phpPhId");
        await Rates("VG", "SELECT vgId, AVG(vgpPrice) FROM vgPackages WHERE vgpPrice > 0 GROUP BY vgId");
        await Rates("FARMER", @"SELECT f.farmFarmerId, AVG(fl.flPrice) FROM farmLodge fl JOIN farm f ON fl.flFarmId = f.farmId
                                WHERE fl.flPrice > 0 GROUP BY f.farmFarmerId");
        await Rates("PJ", "SELECT pPjId, AVG(pStockPrice) FROM packages WHERE pStockPrice > 0 GROUP BY pPjId");

        // ---- completed hunts
        async Task Completed(string role, string sql) => await TryReadAsync(conn, sql, r =>
        {
            if (!r.IsDBNull(0) && Entity(role, r.GetInt32(0)) is { } p) p.CompletedHunts = r.GetInt32(1);
        });
        await Completed("PH", "SELECT bookPhId, COUNT(*) FROM bookings WHERE bookStatus = 'Completed' AND bookPhId IS NOT NULL GROUP BY bookPhId");
        await Completed("VG", "SELECT bookVgId, COUNT(*) FROM bookings WHERE bookStatus = 'Completed' AND bookVgId IS NOT NULL GROUP BY bookVgId");
        await Completed("PJ", @"SELECT p.pPjId, COUNT(*) FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                                WHERE b.bookStatus = 'Completed' GROUP BY p.pPjId");
        await Completed("FARMER", @"SELECT f.farmFarmerId, COUNT(*) FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                                    JOIN farm f ON p.pFarmId = f.farmId WHERE b.bookStatus = 'Completed' GROUP BY f.farmFarmerId");

        // ---- hunts a PJ and a member actually worked together
        async Task Joint(string memberRole, string sql) => await TryReadAsync(conn, sql, r =>
        {
            if (r.IsDBNull(0) || r.IsDBNull(1)) return;
            if (Entity("PJ", r.GetInt32(0)) is { } pj && Entity(memberRole, r.GetInt32(1)) is { } m)
                g.JointHunts[(pj.UserRoleId, m.UserRoleId)] = r.GetInt32(2);
        });
        await Joint("PH", $@"SELECT p.pPjId, b.bookPhId, COUNT(*) FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                             WHERE b.bookStatus IN {Worked} GROUP BY p.pPjId, b.bookPhId");
        await Joint("VG", $@"SELECT p.pPjId, b.bookVgId, COUNT(*) FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                             WHERE b.bookStatus IN {Worked} GROUP BY p.pPjId, b.bookVgId");
        await Joint("FARMER", $@"SELECT p.pPjId, f.farmFarmerId, COUNT(*) FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
                                 JOIN farm f ON p.pFarmId = f.farmId WHERE b.bookStatus IN {Worked} GROUP BY p.pPjId, f.farmFarmerId");

        // ---- existing teams and invites (keyed by userRoleId already)
        await TryReadAsync(conn, "SELECT teamPjId, teamMemberId FROM team WHERE teamPjId IS NOT NULL AND teamMemberId IS NOT NULL",
            r => g.TeamRows.Add((r.GetInt32(0), r.GetInt32(1))));
        await TryReadAsync(conn, "SELECT invitePjId, inviteMemberId, inviteStatus FROM teamInvites",
            r => g.Invites[(r.GetInt32(0), r.GetInt32(1))] = r.IsDBNull(2) ? "" : r.GetString(2));

        return g;
    }

    private static async Task TryReadAsync(SqlConnection conn, string sql, Action<SqlDataReader> row)
    {
        try
        {
            using var cmd = new SqlCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) row(r);
        }
        catch (SqlException) { /* optional signal - skip it */ }
        catch (InvalidCastException) { /* unexpected column type in an older schema - skip it */ }
    }
}
