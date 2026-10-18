using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Runs every ResourceOwnership_db lookup against a real SQL Server, over a small graph
/// of throwaway rows (PJ, farmer, hunter, PH, VG and an outsider, plus a farm, package,
/// booking, PH pricing, VG package, animal, weapon document, story, team row and invite),
/// and checks each returns exactly the right owners - and never the outsider. The unit
/// tests mock this class, so this is what proves the SQL and its joins are correct.
/// Self-skips when no local SQL Server is reachable; removes everything it created.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class ResourceOwnershipIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private bool _dbAvailable;
    private Exception? _seedError;

    private int _pjUser, _farmerUser, _hunterUser, _phUser, _vgUser, _outsiderUser;
    private int _pjUr, _farmerUr, _hunterUr, _phUr, _vgUr;
    private int _pjId, _farmerId, _phId, _vgId;
    private int _farmId, _packageId, _bookingId, _phPricingId, _vgPackageId, _animalId, _weaponUdId, _storyId, _teamId, _inviteId;

    public ResourceOwnershipIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync();
            _dbAvailable = true;
        }
        catch
        {
            return; // no reachable SQL Server - the Facts self-skip
        }

        try
        {

            _pjUser = await InsertUser(conn, "Pj");
            _farmerUser = await InsertUser(conn, "Boer");
            _hunterUser = await InsertUser(conn, "Jagter");
            _phUser = await InsertUser(conn, "Ph");
            _vgUser = await InsertUser(conn, "Vg");
            _outsiderUser = await InsertUser(conn, "Buitestaander");

            _pjUr = await Scalar(conn, $"INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES ({_pjUser}, {await RoleId(conn, "PJ")})");
            _farmerUr = await Scalar(conn, $"INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES ({_farmerUser}, {await RoleId(conn, "FARMER")})");
            _hunterUr = await Scalar(conn, $"INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES ({_hunterUser}, {await RoleId(conn, "HUNTER")})");
            _phUr = await Scalar(conn, $"INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES ({_phUser}, {await RoleId(conn, "PH")})");
            _vgUr = await Scalar(conn, $"INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES ({_vgUser}, {await RoleId(conn, "VG")})");

            _pjId = await Scalar(conn, $"INSERT INTO pj (pjUserRoleId) OUTPUT inserted.pjId VALUES ({_pjUr})");
            _farmerId = await Scalar(conn, $"INSERT INTO farmer (fUserRoleId) OUTPUT inserted.farmerId VALUES ({_farmerUr})");
            _phId = await Scalar(conn, $"INSERT INTO ph (phUserRoleId) OUTPUT inserted.phId VALUES ({_phUr})");
            _vgId = await Scalar(conn, $"INSERT INTO vg (vgUserRoleId) OUTPUT inserted.vgId VALUES ({_vgUr})");

            _farmId = await Scalar(conn, $"INSERT INTO farm (farmFarmerId, farmName) OUTPUT inserted.farmId VALUES ({_farmerId}, 'OwnershipTest Plaas')");
            _packageId = await Scalar(conn, $"INSERT INTO packages (pTitle, pFarmId, pPjId, pCreatedTimestamp) OUTPUT inserted.packageId VALUES ('OwnershipTest', {_farmId}, {_pjId}, GETDATE())");
            _bookingId = await Scalar(conn, $@"INSERT INTO bookings (bookHunterId, bookPackageId, bookPhId, bookVgId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
                OUTPUT inserted.bookingId VALUES ({_hunterUr}, {_packageId}, {_phId}, {_vgId}, '2026-03-01', '2026-03-05', 'Pending', 1, 0)");
            _phPricingId = await Scalar(conn, $"INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) OUTPUT inserted.phPricingId VALUES ({_phId}, 'Vlakte', 1, 100)");
            _vgPackageId = await Scalar(conn, $"INSERT INTO vgPackages (vgId, vgpTitle, vgpPrice) OUTPUT inserted.vgPackageId VALUES ({_vgId}, 'OwnershipTest', 100)");
            _animalId = await Scalar(conn, $"INSERT INTO availableAnimals (aaFarmId, aaName) OUTPUT inserted.aaId VALUES ({_farmId}, 'Koedoe')");
            _weaponUdId = await Scalar(conn, $"INSERT INTO userDocumentation (udUserRole, udName, udStatus, udDtId) OUTPUT inserted.udId VALUES ({_hunterUr}, 'OwnershipTest Geweer', 'Pending', 5)");
            _storyId = await Scalar(conn, $"INSERT INTO storyData (sdBookingId) OUTPUT inserted.storyDataId VALUES ({_bookingId})");
            _teamId = await Scalar(conn, $"INSERT INTO team (teamPjId, teamMemberId) OUTPUT inserted.teamId VALUES ({_pjUr}, {_phUr})");
            _inviteId = await Scalar(conn, $"INSERT INTO teamInvites (invitePjId, inviteMemberId) OUTPUT inserted.inviteId VALUES ({_pjUr}, {_vgUr})");
        }
        catch (Exception ex)
        {
            // The database is reachable but seeding failed (e.g. a schema change) - surface
            // it in the Facts instead of letting them pass vacuously. DisposeAsync still
            // removes whatever was created.
            _seedError = ex;
        }
    }

    private static async Task<int> InsertUser(SqlConnection conn, string surname)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId
            VALUES ('OwnershipTest', @s, CONCAT(@s, '.', NEWID(), '@test.local'), 'x', '0820000099', 1, 1, SYSUTCDATETIME(), 0)", conn);
        cmd.Parameters.AddWithValue("@s", surname);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<int> RoleId(SqlConnection conn, string roleCode)
    {
        using var cmd = new SqlCommand("SELECT TOP 1 roleId FROM roles WHERE roleCode = @c", conn);
        cmd.Parameters.AddWithValue("@c", roleCode);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> Scalar(SqlConnection conn, string sql)
    {
        using var cmd = new SqlCommand(sql, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        try { await conn.OpenAsync(); } catch { return; }

        async Task Exec(string sql)
        {
            try { using var cmd = new SqlCommand(sql, conn); await cmd.ExecuteNonQueryAsync(); }
            catch { /* keep cleaning up the remaining rows */ }
        }

        var users = string.Join(",", new[] { _pjUser, _farmerUser, _hunterUser, _phUser, _vgUser, _outsiderUser }.Where(id => id > 0));
        if (users.Length == 0) return;
        var roleRows = $"(SELECT userRoleId FROM userRoles WHERE urUserId IN ({users}))";

        await Exec($"DELETE FROM teamInvites WHERE invitePjId IN {roleRows} OR inviteMemberId IN {roleRows}");
        await Exec($"DELETE FROM team WHERE teamPjId IN {roleRows} OR teamMemberId IN {roleRows}");
        await Exec($"DELETE FROM storyData WHERE storyDataId = {_storyId}");
        await Exec($"DELETE FROM userDocumentation WHERE udUserRole IN {roleRows}");
        await Exec($"DELETE FROM availableAnimals WHERE aaId = {_animalId}");
        await Exec($"DELETE FROM vgPackages WHERE vgPackageId = {_vgPackageId}");
        await Exec($"DELETE FROM phPricing WHERE phPricingId = {_phPricingId}");
        await Exec($"DELETE FROM bookings WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM packages WHERE packageId = {_packageId}");
        await Exec($"DELETE FROM farm WHERE farmId = {_farmId}");
        await Exec($"DELETE FROM pj WHERE pjUserRoleId IN {roleRows}");
        await Exec($"DELETE FROM farmer WHERE fUserRoleId IN {roleRows}");
        await Exec($"DELETE FROM ph WHERE phUserRoleId IN {roleRows}");
        await Exec($"DELETE FROM vg WHERE vgUserRoleId IN {roleRows}");
        await Exec($"DELETE FROM userRoles WHERE urUserId IN ({users})");
        await Exec($"DELETE FROM users WHERE userId IN ({users})");
    }

    [Fact]
    public async Task EveryLookup_ReturnsExactlyTheRealOwners()
    {
        if (!_dbAvailable) return;
        if (_seedError != null) throw new InvalidOperationException("Seeding the ownership graph failed", _seedError);
        var o = new ResourceOwnership_db(_connectionString);

        Assert.Equal(_hunterUser, await o.GetUserIdForUserRole(_hunterUr));
        Assert.Equal(_farmerUr, await o.GetFarmerUserRoleIdForUser(_farmerUser));
        Assert.Null(await o.GetFarmerUserRoleIdForUser(_outsiderUser));

        Assert.Equal(new HashSet<int> { _farmerUser }, await o.GetFarmOwnerUserIds(_farmId));
        Assert.Equal(new HashSet<int> { _farmerUser }, await o.GetAnimalOwnerUserIds(_animalId));
        Assert.Equal(new HashSet<int> { _pjUser, _farmerUser }, await o.GetPackageOwnerUserIds(_packageId));
        Assert.Equal(new HashSet<int> { _phUser }, await o.GetPhPricingOwnerUserIds(_phPricingId));
        Assert.Equal(new HashSet<int> { _vgUser }, await o.GetVgPackageOwnerUserIds(_vgPackageId));
        Assert.Equal(new HashSet<int> { _hunterUser }, await o.GetUserDocumentOwnerUserIds(_weaponUdId));

        var participants = new HashSet<int> { _hunterUser, _phUser, _vgUser, _pjUser, _farmerUser };
        Assert.Equal(participants, await o.GetBookingParticipantUserIds(_bookingId));
        Assert.Equal(participants, await o.GetStoryOwnerUserIds(_storyId));

        Assert.Equal(new HashSet<int> { _pjUser }, await o.GetTeamOwnerUserIds(_teamId));
        Assert.Equal(new HashSet<int> { _phUser }, await o.GetTeamMemberUserIds(_teamId));
        Assert.Equal(new HashSet<int> { _pjUser }, await o.GetInviteSenderUserIds(_inviteId));
        Assert.Equal(new HashSet<int> { _vgUser }, await o.GetInviteRecipientUserIds(_inviteId));
    }

    [Fact]
    public async Task MissingRows_HaveNoOwners()
    {
        if (!_dbAvailable) return;
        if (_seedError != null) throw new InvalidOperationException("Seeding the ownership graph failed", _seedError);
        var o = new ResourceOwnership_db(_connectionString);

        Assert.Null(await o.GetUserIdForUserRole(-1));
        Assert.Empty(await o.GetPackageOwnerUserIds(-1));
        Assert.Empty(await o.GetFarmOwnerUserIds(-1));
        Assert.Empty(await o.GetBookingParticipantUserIds(-1));
        Assert.Empty(await o.GetStoryOwnerUserIds(-1));
    }
}
