using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves the PH/VG staff-assignment acceptance flow end to end against a real SQL
/// Server: assigning staff no longer approves the booking outright (bookStatus goes
/// to PendingStaffAcceptance, not Approved, and package slots are NOT decremented
/// yet) - the booking only flips to Approved, with the slot decrement firing exactly
/// once, once every currently-assigned role has accepted. Also proves a decline
/// clears that role off the booking without touching the other role's already-
/// accepted assignment, and that reassigning after a decline produces a clean single
/// approval with the full Declined-then-Accepted history intact. Like
/// RatingConcurrencyIntegrationTests and UserDocumentsIntegrationTests this needs a
/// real local SQL Server (a Moq'd Bookings_db can't catch a real transaction/SQL
/// logic bug here) - self-skips if none is reachable. Seeds and tears down its own
/// throwaway users/roles/booking; restores the shared package's slot count exactly
/// so this test leaves no trace on shared dev data.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class BookingStaffAcceptanceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private int _hunterUserId, _phUserId, _ph2UserId, _vgUserId;
    private int _bookingId;
    private int _packageId;
    private int _originalMaxParticipants;
    private bool _dbAvailable;

    public BookingStaffAcceptanceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _dbAvailable = true;

            _hunterUserId = await InsertUser(conn, "StaffAcceptTest", "Hunter", "0820000010");
            _phUserId = await InsertUser(conn, "StaffAcceptTest", "Ph", "0820000011");
            _ph2UserId = await InsertUser(conn, "StaffAcceptTest", "PhTwo", "0820000012");
            _vgUserId = await InsertUser(conn, "StaffAcceptTest", "Vg", "0820000013");

            int hunterRoleId = await GetOrCreateRoleId(conn, "HUNTER", "Jagter");
            int phRoleId = await GetOrCreateRoleId(conn, "PH", "Professionele Jagter");
            int vgRoleId = await GetOrCreateRoleId(conn, "VG", "Videograaf");

            int hunterUserRoleId = await InsertUserRole(conn, _hunterUserId, hunterRoleId);
            await InsertUserRole(conn, _phUserId, phRoleId);
            await InsertUserRole(conn, _ph2UserId, phRoleId);
            await InsertUserRole(conn, _vgUserId, vgRoleId);

            using (var cmd = new SqlCommand("SELECT TOP 1 packageId, pMaxParticipants FROM packages", conn))
            {
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    _packageId = r.GetInt32(0);
                    _originalMaxParticipants = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                }
            }

            // The only seeded package in a fresh dev DB may already be at 0 slots
            // (fully booked) - pin it to a known positive value for the duration of
            // this test so the decrement assertions are deterministic, then restore
            // the real value in DisposeAsync.
            using (var cmd = new SqlCommand("UPDATE packages SET pMaxParticipants = 5 WHERE packageId = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", _packageId);
                await cmd.ExecuteNonQueryAsync();
            }

            // bookWantsVideographer = 1: these tests exercise the PH+VG acceptance flow,
            // which since the vgRequired fix only waits on VG when the hunter actually
            // asked for one (previously it inferred this from bookVgId being set, which
            // wrongly stopped requiring a VG the moment one declined and was cleared).
            using (var cmd = new SqlCommand(@"
                INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests, bookWantsVideographer)
                OUTPUT inserted.bookingId
                VALUES (@hunterUrId, @packageId, '2026-03-01', '2026-03-05', 'Pending', 1, 0, 1)", conn))
            {
                cmd.Parameters.AddWithValue("@hunterUrId", hunterUserRoleId);
                cmd.Parameters.AddWithValue("@packageId", _packageId);
                _bookingId = (int)(await cmd.ExecuteScalarAsync())!;
            }
        }
        catch
        {
            // No local SQL Server reachable - this test class self-skips (see the
            // Facts below) rather than failing the whole suite on a machine without
            // LocalDB.
            _dbAvailable = false;
        }
    }

    private static async Task<int> InsertUser(SqlConnection conn, string name, string surname, string phone)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId
            VALUES (@name, @surname, CONCAT(@surname, '.', NEWID(), '@test.local'), 'x', @phone, 1, 1, SYSUTCDATETIME(), 0)", conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@surname", surname);
        cmd.Parameters.AddWithValue("@phone", phone);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<int> InsertUserRole(SqlConnection conn, int userId, int roleId)
    {
        using var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@r", roleId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<int> GetOrCreateRoleId(SqlConnection conn, string roleCode, string roleName)
    {
        using (var cmd = new SqlCommand("SELECT roleId FROM roles WHERE roleCode = @c", conn))
        {
            cmd.Parameters.AddWithValue("@c", roleCode);
            var existing = await cmd.ExecuteScalarAsync();
            if (existing != null) return (int)existing;
        }
        using (var cmd = new SqlCommand("INSERT INTO roles (roleCode, roleName) OUTPUT inserted.roleId VALUES (@c, @n)", conn))
        {
            cmd.Parameters.AddWithValue("@c", roleCode);
            cmd.Parameters.AddWithValue("@n", roleName);
            return (int)(await cmd.ExecuteScalarAsync())!;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        async Task Exec(string sql)
        {
            try
            {
                using var cmd = new SqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Keep going - one cleanup step failing must not strand every step
                // after it, leaving this test's scaffold rows behind permanently.
            }
        }

        // Undo the finalize step's slot decrement so this test leaves the shared
        // package exactly as it found it.
        await Exec($"UPDATE packages SET pMaxParticipants = {_originalMaxParticipants} WHERE packageId = {_packageId}");
        await Exec($"DELETE FROM bookingAuditLogs WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM bookingStaffAssignments WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM userNotifications WHERE recipientUserId IN ({_hunterUserId}, {_phUserId}, {_ph2UserId}, {_vgUserId})");
        await Exec($"DELETE FROM bookings WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId IN ({_phUserId}, {_ph2UserId}))");
        await Exec($"DELETE FROM vg WHERE vgUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId = {_vgUserId})");
        await Exec($"DELETE FROM userRoles WHERE urUserId IN ({_hunterUserId}, {_phUserId}, {_ph2UserId}, {_vgUserId})");
        await Exec($"DELETE FROM users WHERE userId IN ({_hunterUserId}, {_phUserId}, {_ph2UserId}, {_vgUserId})");
    }

    [Fact]
    public async Task AssignThenBothAccept_ApprovesExactlyOnceWithSingleSlotDecrement()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);

        bool assigned = await db.ApproveBookingWithStaff(_bookingId, _phUserId, _vgUserId);
        Assert.True(assigned);
        Assert.Equal("PendingStaffAcceptance", await GetBookingStatus());

        var (phResult, phApproved) = await db.RespondToStaffAssignment(_bookingId, "PH", _phUserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, phResult);
        Assert.False(phApproved);
        Assert.Equal("PendingStaffAcceptance", await GetBookingStatus());

        var (vgResult, vgApproved) = await db.RespondToStaffAssignment(_bookingId, "VG", _vgUserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, vgResult);
        Assert.True(vgApproved);
        Assert.Equal("Approved", await GetBookingStatus());

        Assert.Equal(4, await GetPackageSlots()); // pinned to 5 in InitializeAsync, minus the one-time decrement
    }

    [Fact]
    public async Task DeclineThenReassignThenAccept_ProducesCleanApprovalWithFullHistory()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        await db.ApproveBookingWithStaff(_bookingId, _phUserId, _vgUserId);

        // VG accepts immediately; PH declines.
        var (vgResult, _) = await db.RespondToStaffAssignment(_bookingId, "VG", _vgUserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, vgResult);

        var (declineResult, declineApproved) = await db.RespondToStaffAssignment(_bookingId, "PH", _phUserId, false, "Nie beskikbaar nie");
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, declineResult);
        Assert.False(declineApproved);
        Assert.Equal("PendingStaffAcceptance", await GetBookingStatus());

        // The original PH can't respond a second time - their row is Declined, not Pending.
        var (staleResult, _) = await db.RespondToStaffAssignment(_bookingId, "PH", _phUserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.NotFound, staleResult);

        // PJ reassigns PH to someone else - VG's already-Accepted row must survive untouched.
        bool reassigned = await db.ApproveBookingWithStaff(_bookingId, _ph2UserId, _vgUserId);
        Assert.True(reassigned);

        var (acceptResult, acceptApproved) = await db.RespondToStaffAssignment(_bookingId, "PH", _ph2UserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, acceptResult);
        Assert.True(acceptApproved); // VG was already Accepted and untouched by the PH reassignment
        Assert.Equal("Approved", await GetBookingStatus());

        // Exactly one slot decrement, not two, despite the decline/reassign detour.
        Assert.Equal(4, await GetPackageSlots()); // pinned to 5 in InitializeAsync, minus the one-time decrement

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT status, assigneeUserId FROM bookingStaffAssignments WHERE bookingId = @id AND staffRole = 'PH' ORDER BY assignmentId", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<(string Status, int Assignee)>();
        while (await r.ReadAsync()) rows.Add((r.GetString(0), r.GetInt32(1)));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Declined", rows[0].Status);
        Assert.Equal(_phUserId, rows[0].Assignee);
        Assert.Equal("Accepted", rows[1].Status);
        Assert.Equal(_ph2UserId, rows[1].Assignee);
    }

    /// <summary>
    /// Proves the vgRequired fix: whether a videographer must accept before the
    /// booking can auto-approve is decided by bookWantsVideographer (what the hunter
    /// actually asked for), not by whether bookVgId happens to be set right now. A VG
    /// decline clears bookVgId - if vgRequired were still derived from that column,
    /// the PH accepting alone would have wrongly auto-approved a booking whose hunter
    /// explicitly wanted a videographer.
    /// </summary>
    [Fact]
    public async Task PhAcceptsAloneAfterVgDeclines_DoesNotAutoApproveWhenVideographerWasRequested()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        await db.ApproveBookingWithStaff(_bookingId, _phUserId, _vgUserId);

        var (vgDeclineResult, vgDeclineApproved) = await db.RespondToStaffAssignment(_bookingId, "VG", _vgUserId, false, "Nie beskikbaar nie");
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, vgDeclineResult);
        Assert.False(vgDeclineApproved);

        var (phResult, phApproved) = await db.RespondToStaffAssignment(_bookingId, "PH", _phUserId, true, null);
        Assert.Equal(Bookings_db.StaffResponseResult.Ok, phResult);
        Assert.False(phApproved); // must still wait for a videographer, even though bookVgId is now NULL
        Assert.Equal("PendingStaffAcceptance", await GetBookingStatus());
        Assert.Equal(5, await GetPackageSlots()); // unchanged - no premature decrement
    }

    /// <summary>
    /// Two accepts landing at nearly the same instant (PH and VG both click "Aanvaar"
    /// around the same time) must not both see the other as "not yet accepted" and
    /// leave a fully-accepted booking stranded at PendingStaffAcceptance. This relies
    /// on SQL Server's default (non-snapshot) locking-read behaviour under READ
    /// COMMITTED: whichever transaction's UPDATE to its own assignment row commits
    /// first, the other transaction's read of that row blocks until the commit, then
    /// sees the fresh value - confirmed this database has RCSI/snapshot isolation OFF
    /// (the default), which this test depends on. If that ever changes, this test
    /// would start failing intermittently and needs revisiting with an explicit lock.
    /// </summary>
    [Fact]
    public async Task SimultaneousPhAndVgAccept_StillFinalizesExactlyOnce()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        await db.ApproveBookingWithStaff(_bookingId, _phUserId, _vgUserId);

        var phTask = db.RespondToStaffAssignment(_bookingId, "PH", _phUserId, true, null);
        var vgTask = db.RespondToStaffAssignment(_bookingId, "VG", _vgUserId, true, null);
        var results = await Task.WhenAll(phTask, vgTask);

        Assert.All(results, r => Assert.Equal(Bookings_db.StaffResponseResult.Ok, r.Result));
        Assert.Equal("Approved", await GetBookingStatus());
        // Exactly one of the two calls should be the one that observes "everyone has
        // now accepted" and runs the finalize step - not zero (stranded) and not both
        // (double slot decrement).
        Assert.Single(results, r => r.BookingNowApproved);
        Assert.Equal(4, await GetPackageSlots());
    }

    /// <summary>
    /// Ten concurrent identical "assign PH" calls (double-click / two open tabs on the
    /// same booking) must produce exactly one Pending assignment row, not ten. There's
    /// no explicit locking hint on the assignment insert path - this is protected
    /// incidentally by the exclusive row lock ApproveBookingWithStaff's own bookings
    /// UPDATE takes on this booking's row earlier in the same transaction, which
    /// serializes the full transaction body (including the assignment insert) across
    /// concurrent callers targeting the same bookingId. That's a real, structural
    /// guarantee (not a timing fluke) as long as the bookings UPDATE keeps running
    /// before AssignStaffRoleAsync in the same transaction.
    /// </summary>
    [Fact]
    public async Task ConcurrentIdenticalAssignCalls_ProduceExactlyOnePendingRow()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        var tasks = Enumerable.Range(0, 10).Select(_ => db.ApproveBookingWithStaff(_bookingId, _phUserId, null));
        await Task.WhenAll(tasks);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM bookingStaffAssignments WHERE bookingId = @id AND staffRole = 'PH' AND status = 'Pending'", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        int pendingRowCount = (int)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(1, pendingRowCount);
    }

    private async Task<string> GetBookingStatus()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT bookStatus FROM bookings WHERE bookingId = @id", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> GetPackageSlots()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT pMaxParticipants FROM packages WHERE packageId = @id", conn);
        cmd.Parameters.AddWithValue("@id", _packageId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
