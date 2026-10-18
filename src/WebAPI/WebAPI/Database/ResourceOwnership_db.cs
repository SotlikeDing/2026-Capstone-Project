using Microsoft.Data.SqlClient;

namespace WebAPI.Database;

/// <summary>
/// Resolves which user(s) own a given row, so controllers can enforce "only the owner
/// (or an Admin) may change this". Every lookup follows the schema's real foreign keys:
/// role tables (farmer/pj/ph/vg/ap) point at userRoles, and userRoles.urUserId is the
/// owning user. team.teamPjId/teamMemberId and bookings.bookHunterId hold userRoleIds;
/// bookings.bookPhId/bookVgId hold ph.phId/vg.vgId; packages.pPjId holds pj.pjId.
/// Methods return an empty set / null when the row doesn't exist.
/// </summary>
public class ResourceOwnership_db
{
    private readonly string _connectionString;

    public ResourceOwnership_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    private async Task<HashSet<int>> QueryIds(string sql, int id)
    {
        var result = new HashSet<int>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (!r.IsDBNull(0)) result.Add(Convert.ToInt32(r.GetValue(0)));
        }
        return result;
    }

    /// <summary>The user a userRoles row belongs to.</summary>
    public virtual async Task<int?> GetUserIdForUserRole(int userRoleId)
    {
        var ids = await QueryIds("SELECT urUserId FROM userRoles WHERE userRoleId = @id", userRoleId);
        return ids.Count == 0 ? null : ids.First();
    }

    /// <summary>The caller's farmer-role userRoleId (farmer.fUserRoleId), or null if they aren't a farmer.</summary>
    public virtual async Task<int?> GetFarmerUserRoleIdForUser(int userId)
    {
        var ids = await QueryIds(@"
            SELECT fa.fUserRoleId FROM farmer fa
            JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId
            WHERE ur.urUserId = @id", userId);
        return ids.Count == 0 ? null : ids.Min();
    }

    /// <summary>
    /// The caller's VG-role userRoleId, or null if they aren't a videographer. Prefers
    /// the VG role row actually assigned to the given booking (vg.vgUserRoleId), so a
    /// user with several VG rows acts with the one the booking knows about.
    /// </summary>
    public virtual async Task<int?> GetVgUserRoleIdForUser(int userId, int bookingId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        const string q = @"
            SELECT TOP 1 ur.userRoleId
            FROM userRoles ur
            JOIN roles r ON r.roleId = ur.urRoleId
            LEFT JOIN bookings b ON b.bookingId = @bookingId
            LEFT JOIN vg ON vg.vgId = b.bookVgId
            WHERE ur.urUserId = @userId AND UPPER(r.roleCode) IN ('VG', 'VIDEOGRAAF', 'VIDEOGRAPHER')
            ORDER BY CASE WHEN vg.vgUserRoleId = ur.userRoleId THEN 0 ELSE 1 END, ur.userRoleId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    /// <summary>The farm's farmer.</summary>
    public virtual Task<HashSet<int>> GetFarmOwnerUserIds(int farmId) => QueryIds(@"
        SELECT ur.urUserId FROM farm f
        JOIN farmer fa ON f.farmFarmerId = fa.farmerId
        JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId
        WHERE f.farmId = @id", farmId);

    /// <summary>The farmer of the farm an availableAnimals row belongs to.</summary>
    public virtual Task<HashSet<int>> GetAnimalOwnerUserIds(int availableAnimalId) => QueryIds(@"
        SELECT ur.urUserId FROM availableAnimals aa
        JOIN farm f ON aa.aaFarmId = f.farmId
        JOIN farmer fa ON f.farmFarmerId = fa.farmerId
        JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId
        WHERE aa.aaId = @id", availableAnimalId);

    /// <summary>The package's organising PJ plus the farmer of the package's farm.</summary>
    public virtual Task<HashSet<int>> GetPackageOwnerUserIds(int packageId) => QueryIds(@"
        SELECT ur.urUserId FROM packages p
        JOIN pj ON p.pPjId = pj.pjId
        JOIN userRoles ur ON pj.pjUserRoleId = ur.userRoleId
        WHERE p.packageId = @id
        UNION
        SELECT ur.urUserId FROM packages p
        JOIN farm f ON p.pFarmId = f.farmId
        JOIN farmer fa ON f.farmFarmerId = fa.farmerId
        JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId
        WHERE p.packageId = @id", packageId);

    /// <summary>The PH a phPricing row belongs to.</summary>
    public virtual Task<HashSet<int>> GetPhPricingOwnerUserIds(int phPricingId) => QueryIds(@"
        SELECT ur.urUserId FROM phPricing pp
        JOIN ph ON pp.phpPhId = ph.phId
        JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId
        WHERE pp.phPricingId = @id", phPricingId);

    /// <summary>The VG a vgPackages row belongs to.</summary>
    public virtual Task<HashSet<int>> GetVgPackageOwnerUserIds(int vgPackageId) => QueryIds(@"
        SELECT ur.urUserId FROM vgPackages vp
        JOIN vg ON vp.vgId = vg.vgId
        JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId
        WHERE vp.vgPackageId = @id", vgPackageId);

    /// <summary>The user a userDocumentation row (documents, permits, weapons) belongs to.</summary>
    public virtual Task<HashSet<int>> GetUserDocumentOwnerUserIds(int udId) => QueryIds(@"
        SELECT ur.urUserId FROM userDocumentation ud
        JOIN userRoles ur ON ud.udUserRole = ur.userRoleId
        WHERE ud.udId = @id", udId);

    /// <summary>Everyone taking part in a booking: hunter, assigned PH and VG, the package's PJ and the farm's farmer.</summary>
    public virtual Task<HashSet<int>> GetBookingParticipantUserIds(int bookingId) => QueryIds(@"
        SELECT ur.urUserId FROM bookings b JOIN userRoles ur ON b.bookHunterId = ur.userRoleId WHERE b.bookingId = @id
        UNION
        SELECT ur.urUserId FROM bookings b JOIN ph ON b.bookPhId = ph.phId JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId WHERE b.bookingId = @id
        UNION
        SELECT ur.urUserId FROM bookings b JOIN vg ON b.bookVgId = vg.vgId JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId WHERE b.bookingId = @id
        UNION
        SELECT ur.urUserId FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
            JOIN pj ON p.pPjId = pj.pjId JOIN userRoles ur ON pj.pjUserRoleId = ur.userRoleId WHERE b.bookingId = @id
        UNION
        SELECT ur.urUserId FROM bookings b JOIN packages p ON b.bookPackageId = p.packageId
            JOIN farm f ON p.pFarmId = f.farmId JOIN farmer fa ON f.farmFarmerId = fa.farmerId
            JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId WHERE b.bookingId = @id", bookingId);

    /// <summary>The participants of the booking a story (storyData row) is about.</summary>
    public virtual async Task<HashSet<int>> GetStoryOwnerUserIds(int storyId)
    {
        var bookingIds = await QueryIds("SELECT sdBookingId FROM storyData WHERE storyDataId = @id", storyId);
        return bookingIds.Count == 0 ? new HashSet<int>() : await GetBookingParticipantUserIds(bookingIds.First());
    }

    /// <summary>The team row's PJ (the team owner).</summary>
    public virtual Task<HashSet<int>> GetTeamOwnerUserIds(int teamId) => QueryIds(@"
        SELECT ur.urUserId FROM team t JOIN userRoles ur ON t.teamPjId = ur.userRoleId WHERE t.teamId = @id", teamId);

    /// <summary>The team row's member.</summary>
    public virtual Task<HashSet<int>> GetTeamMemberUserIds(int teamId) => QueryIds(@"
        SELECT ur.urUserId FROM team t JOIN userRoles ur ON t.teamMemberId = ur.userRoleId WHERE t.teamId = @id", teamId);

    /// <summary>The PJ who sent a team invite.</summary>
    public virtual Task<HashSet<int>> GetInviteSenderUserIds(int inviteId) => QueryIds(@"
        SELECT ur.urUserId FROM teamInvites ti JOIN userRoles ur ON ti.invitePjId = ur.userRoleId WHERE ti.inviteId = @id", inviteId);

    /// <summary>The user a team invite was sent to.</summary>
    public virtual Task<HashSet<int>> GetInviteRecipientUserIds(int inviteId) => QueryIds(@"
        SELECT ur.urUserId FROM teamInvites ti JOIN userRoles ur ON ti.inviteMemberId = ur.userRoleId WHERE ti.inviteId = @id", inviteId);
}
