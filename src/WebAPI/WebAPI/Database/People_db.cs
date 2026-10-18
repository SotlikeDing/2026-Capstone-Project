using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Public "recruit"/discovery profiles for PJ, PH, VG and Farmer roles: a public
/// browse-by-role list (verification-gated - see RequiredDocsApprovedPredicate) and
/// a direct by-id lookup (not gated - already-known context, not discovery).
/// </summary>
public class People_db
{
    private readonly string _cs;
    public People_db(string cs) => _cs = cs;

    /// <summary>Lists verified public profiles for a role code (PH/VG/Boer/FARMER/PJ). Unknown role codes return an empty list.</summary>
    public async Task<List<PublicProfileResponse>> GetPeople(string roleCode)
    {
        string? q = roleCode switch
        {
            "PH"   => PhSql(false),
            "VG"   => VgSql(false),
            "Boer" => BoerSql(false),
            "FARMER" => BoerSql(false),
            "PJ"   => PjSql(false),
            _      => null
        };
        if (q is null) return [];

        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PublicProfileResponse>();
        while (await r.ReadAsync())
            list.Add(Map(r));
        return list;
    }

    /// <summary>Fetches a single public profile by userRoleId, trying all four role queries via UNION ALL (a userRoleId only ever matches one).</summary>
    public async Task<PublicProfileResponse?> GetPerson(int userRoleId)
    {
        string q = $@"
{PhSql(true)}
UNION ALL
{VgSql(true)}
UNION ALL
{BoerSql(true)}
UNION ALL
{PjSql(true)}";

        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Map(r) : null;
    }

    /// <summary>
    /// Excludes a role with an outstanding required document (still Pending or
    /// Rejected) from public discovery. `{alias}` is the query's userRoles alias.
    /// Only applied to the unfiltered list (byId: false) - GetPerson(id) is a direct,
    /// already-known-id lookup (an owner viewing their own profile, or a counterparty
    /// on an existing booking), not discovery, so it must keep resolving regardless of
    /// verification status; only GetPeople's public browse/search actually gates.
    /// </summary>
    private static string RequiredDocsApprovedPredicate(string alias, bool byId) => byId ? "1 = 1" : $@"
        NOT EXISTS (
            SELECT 1 FROM roleDocumentRequirements rdr
            JOIN roles rox ON rox.roleId = {alias}.urRoleId
            WHERE rdr.roleCode = rox.roleCode AND rdr.isRequired = 1
              AND NOT EXISTS (
                  SELECT 1 FROM userDocumentation ud
                  WHERE ud.udUserRole = {alias}.userRoleId AND ud.udDtId = rdr.documentTypeId
                    AND ud.udIsDeleted = 0 AND ud.udStatus = 'Approved'
              )
        )";

    // Columns (0-13): userRoleId, userId, name, surname, roleCode, roleName,
    //                 bio, location, avgRating, ratingCount, isPublic,
    //                 publicUntil, contactEmail, contactPhone
    /// <summary>Builds the PH profile query (public-profile column shape above).</summary>
    private static string PhSql(bool byId) => $@"
        SELECT ur.userRoleId, u.userId, u.uName, u.uSurname, r.roleCode, r.roleName,
               ph.phBio,
               CAST(NULL AS NVARCHAR(MAX)) AS location,
               (SELECT AVG(CAST(stars AS FLOAT)) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               (SELECT COUNT(*) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               CAST(CASE WHEN ph.phPubliclyVisibleTill > GETDATE() THEN 1 ELSE 0 END AS BIT),
               ph.phPubliclyVisibleTill,
               u.uEmail, u.uPhone
        FROM userRoles ur
        JOIN users u ON ur.urUserId = u.userId
        JOIN roles r ON ur.urRoleId = r.roleId
        LEFT JOIN ph ON ph.phUserRoleId = ur.userRoleId
        WHERE r.roleCode = 'PH' {(byId ? "AND ur.userRoleId = @id" : "")} AND {RequiredDocsApprovedPredicate("ur", byId)}";

    /// <summary>Builds the VG profile query. VG has no bio column in the schema, so Bio is always NULL for this role.</summary>
    private static string VgSql(bool byId) => $@"
        SELECT ur.userRoleId, u.userId, u.uName, u.uSurname, r.roleCode, r.roleName,
               CAST(NULL AS NVARCHAR(MAX)) AS bio,
               CAST(NULL AS NVARCHAR(MAX)) AS location,
               (SELECT AVG(CAST(stars AS FLOAT)) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               (SELECT COUNT(*) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               CAST(CASE WHEN vg.vgPubliclyVisibleTill > GETDATE() THEN 1 ELSE 0 END AS BIT),
               vg.vgPubliclyVisibleTill,
               u.uEmail, u.uPhone
        FROM userRoles ur
        JOIN users u ON ur.urUserId = u.userId
        JOIN roles r ON ur.urRoleId = r.roleId
        LEFT JOIN vg ON vg.vgUserRoleId = ur.userRoleId
        WHERE r.roleCode = 'VG' {(byId ? "AND ur.userRoleId = @id" : "")} AND {RequiredDocsApprovedPredicate("ur", byId)}";

    /// <summary>Builds the Farmer profile query. Location is derived from the farmer's first farm's address (MIN(farmAddressId) per farmer), not a location field of their own.</summary>
    private static string BoerSql(bool byId) => $@"
        SELECT ur.userRoleId, u.userId, u.uName, u.uSurname, r.roleCode, r.roleName,
               fa.fBio,
               CASE
                   WHEN a.addressCity IS NOT NULL AND a.addressRegion IS NOT NULL THEN a.addressCity + ', ' + a.addressRegion
                   WHEN a.addressCity IS NOT NULL THEN a.addressCity
                   ELSE a.addressRegion
               END AS location,
               (SELECT AVG(CAST(stars AS FLOAT)) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               (SELECT COUNT(*) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               CAST(CASE WHEN fa.fPubliclyVisibleTill > GETDATE() THEN 1 ELSE 0 END AS BIT),
               fa.fPubliclyVisibleTill,
               u.uEmail, u.uPhone
        FROM userRoles ur
        JOIN users u ON ur.urUserId = u.userId
        JOIN roles r ON ur.urRoleId = r.roleId
        LEFT JOIN farmer fa ON fa.fUserRoleId = ur.userRoleId
        LEFT JOIN (SELECT farmFarmerId, MIN(farmAddressId) AS addrId FROM farm GROUP BY farmFarmerId) ff
            ON ff.farmFarmerId = fa.farmerId
        LEFT JOIN addresses a ON a.addressId = ff.addrId
        WHERE (r.roleCode = 'Boer' OR r.roleCode = 'FARMER') {(byId ? "AND ur.userRoleId = @id" : "")} AND {RequiredDocsApprovedPredicate("ur", byId)}";

    /// <summary>Builds the PJ profile query. PJ has no location column in the schema, so Location is always NULL for this role.</summary>
    private static string PjSql(bool byId) => $@"
        SELECT ur.userRoleId, u.userId, u.uName, u.uSurname, r.roleCode, r.roleName,
               pj.pjBio,
               CAST(NULL AS NVARCHAR(MAX)) AS location,
               (SELECT AVG(CAST(stars AS FLOAT)) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               (SELECT COUNT(*) FROM ratings WHERE rateeUserId = ur.urUserId AND rateeRoleCode = r.roleCode),
               CAST(CASE WHEN pj.pjPubliclyVisibleTill > GETDATE() THEN 1 ELSE 0 END AS BIT),
               pj.pjPubliclyVisibleTill,
               u.uEmail, u.uPhone
        FROM userRoles ur
        JOIN users u ON ur.urUserId = u.userId
        JOIN roles r ON ur.urRoleId = r.roleId
        LEFT JOIN pj ON pj.pjUserRoleId = ur.userRoleId
        WHERE r.roleCode = 'PJ' {(byId ? "AND ur.userRoleId = @id" : "")} AND {RequiredDocsApprovedPredicate("ur", byId)}";

    /// <summary>Maps one result row (from any of the four role queries - they share a column shape) to a PublicProfileResponse.</summary>
    private static PublicProfileResponse Map(SqlDataReader r) => new()
    {
        UserRoleId    = r.GetInt32(0),
        UserId        = r.GetInt32(1),
        Name          = r.GetString(2),
        Surname       = r.GetString(3),
        RoleCode      = r.GetString(4),
        RoleName      = r.GetString(5),
        Bio           = r.IsDBNull(6)  ? null : r.GetString(6),
        Location      = r.IsDBNull(7)  ? null : r.GetString(7),
        AverageRating = r.IsDBNull(8)  ? null : r.GetDouble(8),
        RatingCount   = r.GetInt32(9),
        IsPublic      = r.GetBoolean(10),
        PublicUntil   = r.IsDBNull(11) ? null : r.GetDateTime(11),
        ContactEmail  = r.IsDBNull(12) ? null : r.GetString(12),
        ContactPhone  = r.IsDBNull(13) ? null : r.GetString(13)
    };
}


