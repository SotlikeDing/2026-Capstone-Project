using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Multi-hunter bookings against a real SQL Server (the rules live in SQL: the booking
/// row lock, the slot count and the unique indexes). Proves: only the main hunter adds,
/// only on a confirmed booking, only hunters, never more than bookHuntGuests - 1; an
/// unknown email becomes an invite that links once that email is verified on a Hunter
/// account; added hunters see the trip without money; and ten simultaneous adds never
/// overfill the booking. Self-skips without a reachable LocalDB, like the other
/// integration tests, and removes every row it creates.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class BookingCompanionsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private readonly List<int> _userIds = new();
    private readonly List<int> _bookingIds = new();
    private int _packageId;
    private int _hunterRoleId, _phRoleId;
    private int _mainUserId, _mainUserRoleId, _friendUserId, _phUserId;
    private bool _dbAvailable;

    public BookingCompanionsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Resolving services starts the host, which runs the migrations (including AddBookingCompanions.sql).
        var config = factory.Services.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("DefaultConnection")!;
    }

    private string Email(string who) => $"{who}.{_run}@companions.test";

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using (var check = new SqlCommand("SELECT OBJECT_ID('dbo.bookingCompanions')", conn))
            {
                if (await check.ExecuteScalarAsync() is DBNull or null) return; // migration not applied: skip
            }
            using (var cmd = new SqlCommand("SELECT TOP 1 packageId FROM packages ORDER BY packageId", conn))
            {
                var pkg = await cmd.ExecuteScalarAsync();
                if (pkg == null) return;
                _packageId = (int)pkg;
            }

            _hunterRoleId = await GetOrCreateRoleId(conn, "HUNTER", "Jagter");
            _phRoleId = await GetOrCreateRoleId(conn, "PH", "Professionele Jagter");

            _mainUserId = await InsertUser(conn, "Main", Email("main"), verified: true);
            _mainUserRoleId = await InsertUserRole(conn, _mainUserId, _hunterRoleId);
            _friendUserId = await InsertUser(conn, "Friend", Email("friend"), verified: true);
            await InsertUserRole(conn, _friendUserId, _hunterRoleId);
            _phUserId = await InsertUser(conn, "Guide", Email("guide"), verified: true);
            await InsertUserRole(conn, _phUserId, _phRoleId);
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    private async Task<int> InsertUser(SqlConnection conn, string name, string email, bool verified)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId
            VALUES (@name, 'CompanionTest', @email, 'x', '0820000099', @verified, 1, SYSUTCDATETIME(), 0)", conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@verified", verified);
        int id = (int)(await cmd.ExecuteScalarAsync())!;
        _userIds.Add(id);
        return id;
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

    /// <summary>A booking of the main hunter for huntGuests hunters (main hunter included).</summary>
    private async Task<int> CreateBooking(string status, int huntGuests)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
            OUTPUT inserted.bookingId
            VALUES (@h, @p, '2026-11-01', '2026-11-05', @s, @g, 2)", conn);
        cmd.Parameters.AddWithValue("@h", _mainUserRoleId);
        cmd.Parameters.AddWithValue("@p", _packageId);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@g", huntGuests);
        int id = (int)(await cmd.ExecuteScalarAsync())!;
        _bookingIds.Add(id);
        return id;
    }

    private async Task SetStatus(int bookingId, string status)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("UPDATE bookings SET bookStatus = @s WHERE bookingId = @id", conn);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@id", bookingId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_userIds.Count == 0 && _bookingIds.Count == 0) return;
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        async Task Exec(string sql)
        {
            try
            {
                using var cmd = new SqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { } // keep cleaning up
        }
        if (_bookingIds.Count > 0)
        {
            string bookings = string.Join(",", _bookingIds);
            await Exec($"DELETE FROM bookingCompanions WHERE bookingId IN ({bookings})");
            await Exec($"DELETE FROM bookings WHERE bookingId IN ({bookings})");
        }
        if (_userIds.Count > 0)
        {
            string users = string.Join(",", _userIds);
            await Exec($"DELETE FROM userRoles WHERE urUserId IN ({users})");
            await Exec($"DELETE FROM users WHERE userId IN ({users})");
        }
    }

    [Fact]
    public async Task Hunters_can_only_be_added_once_the_booking_is_confirmed_and_only_by_the_main_hunter()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Pending", huntGuests: 3);

        Assert.Equal(AddCompanionOutcome.NotConfirmed, (await db.AddCompanion(bookingId, _mainUserId, false, "Friend", Email("friend"))).Outcome);
        await SetStatus(bookingId, "PendingStaffAcceptance");
        Assert.Equal(AddCompanionOutcome.NotConfirmed, (await db.AddCompanion(bookingId, _mainUserId, false, "Friend", Email("friend"))).Outcome);

        await SetStatus(bookingId, "Approved");
        Assert.Equal(AddCompanionOutcome.NotMainHunter, (await db.AddCompanion(bookingId, _friendUserId, false, "Me", Email("friend"))).Outcome);
        Assert.Equal(AddCompanionOutcome.Linked, (await db.AddCompanion(bookingId, _mainUserId, false, "Friend", Email("friend"))).Outcome);

        await SetStatus(bookingId, "Paid");
        Assert.Equal(AddCompanionOutcome.Invited, (await db.AddCompanion(bookingId, _mainUserId, false, "New Person", Email("newperson"))).Outcome);
    }

    [Fact]
    public async Task Only_hunters_up_to_the_hunter_count_and_never_twice()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Approved", huntGuests: 3); // main + 2

        Assert.Equal(AddCompanionOutcome.NotAHunter, (await db.AddCompanion(bookingId, _mainUserId, false, "Guide", Email("guide"))).Outcome);
        Assert.Equal(AddCompanionOutcome.IsMainHunter, (await db.AddCompanion(bookingId, _mainUserId, false, "Me", Email("main").ToUpperInvariant())).Outcome);

        var linked = await db.AddCompanion(bookingId, _mainUserId, false, "Typed Name", Email("friend"));
        Assert.Equal(AddCompanionOutcome.Linked, linked.Outcome);
        Assert.Equal("Friend CompanionTest", linked.Hunter!.Name); // the account's real name, not what was typed
        Assert.Equal(AddCompanionOutcome.AlreadyAdded, (await db.AddCompanion(bookingId, _mainUserId, false, "Friend", "  " + Email("friend").ToUpperInvariant() + " ")).Outcome);

        Assert.Equal(AddCompanionOutcome.Invited, (await db.AddCompanion(bookingId, _mainUserId, false, "Second", Email("second"))).Outcome);
        Assert.Equal(AddCompanionOutcome.Full, (await db.AddCompanion(bookingId, _mainUserId, false, "Third", Email("third"))).Outcome);

        var (roster, onBooking) = await db.GetBookingHunters(bookingId, _mainUserId, false);
        Assert.True(onBooking);
        Assert.True(roster!.CanManage);
        Assert.Equal(3, roster.MaxHunters);
        Assert.Equal(0, roster.OpenSlots);
        Assert.Equal(new[] { "Main", "Linked", "Pending" }, roster.Hunters.Select(h => h.Status));
    }

    [Fact]
    public async Task An_invite_links_itself_once_the_email_is_verified_on_a_hunter_account()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Approved", huntGuests: 2);
        Assert.Equal(AddCompanionOutcome.Invited, (await db.AddCompanion(bookingId, _mainUserId, false, "Late Joiner", Email("late"))).Outcome);

        int lateUserId;
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            lateUserId = await InsertUser(conn, "Late", Email("late"), verified: false);
            await InsertUserRole(conn, lateUserId, _hunterRoleId);
        }
        Assert.Equal(0, await db.ClaimPendingInvitesForEmail(Email("late"))); // not verified yet
        Assert.Empty(await db.GetCompanionBookings(lateUserId));

        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            using var cmd = new SqlCommand("UPDATE users SET uIsEmailVerified = 1 WHERE userId = @u", conn);
            cmd.Parameters.AddWithValue("@u", lateUserId);
            await cmd.ExecuteNonQueryAsync();
        }
        Assert.Equal(1, await db.ClaimPendingInvitesForEmail(Email("late")));
        Assert.Equal(0, await db.ClaimPendingInvitesForEmail(Email("late"))); // idempotent

        var trips = await db.GetCompanionBookings(lateUserId);
        var trip = Assert.Single(trips);
        Assert.Equal(bookingId, trip.Id);
        Assert.Equal("Main CompanionTest", trip.MainHunterName);
        Assert.DoesNotContain(typeof(CompanionBookingResponse).GetProperties(), p =>
            p.Name.Contains("Price") || p.Name.Contains("Amount") || p.Name.Contains("Payment"));
    }

    [Fact]
    public async Task Added_hunters_see_the_roster_without_emails_and_can_leave()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Approved", huntGuests: 3);
        var added = await db.AddCompanion(bookingId, _mainUserId, false, "Friend", Email("friend"));

        var (roster, onBooking) = await db.GetBookingHunters(bookingId, _friendUserId, false);
        Assert.True(onBooking);
        Assert.False(roster!.CanManage);
        Assert.All(roster.Hunters, h => Assert.Null(h.Email));
        Assert.True(roster.Hunters.Single(h => h.IsYou).CompanionId == added.Hunter!.CompanionId);

        var (_, guideOnBooking) = await db.GetBookingHunters(bookingId, _phUserId, false);
        Assert.False(guideOnBooking);
        Assert.Equal(RemoveCompanionOutcome.Forbidden, await db.RemoveCompanion(bookingId, added.Hunter.CompanionId, _phUserId, false));

        Assert.Equal(RemoveCompanionOutcome.Removed, await db.RemoveCompanion(bookingId, added.Hunter.CompanionId, _friendUserId, false));
        Assert.Empty(await db.GetCompanionBookings(_friendUserId));
        Assert.Equal(RemoveCompanionOutcome.NotFound, await db.RemoveCompanion(bookingId, added.Hunter.CompanionId, _mainUserId, false));
    }

    [Fact]
    public async Task The_roster_is_locked_once_the_hunt_is_completed()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Approved", huntGuests: 2);
        var added = await db.AddCompanion(bookingId, _mainUserId, false, "Friend", Email("friend"));
        await SetStatus(bookingId, "Completed");

        Assert.Equal(RemoveCompanionOutcome.Locked, await db.RemoveCompanion(bookingId, added.Hunter!.CompanionId, _mainUserId, false));
        Assert.Equal(AddCompanionOutcome.NotConfirmed, (await db.AddCompanion(bookingId, _mainUserId, false, "Late", Email("late2"))).Outcome);
        Assert.Single(await db.GetCompanionBookings(_friendUserId)); // still on the trip's history
    }

    [Fact]
    public async Task Ten_simultaneous_adds_never_overfill_the_booking()
    {
        if (!_dbAvailable) return;
        var db = new BookingCompanions_db(_connectionString);
        int bookingId = await CreateBooking("Approved", huntGuests: 3); // 2 open slots

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            db.AddCompanion(bookingId, _mainUserId, false, $"Racer {i}", Email($"racer{i}"))));

        Assert.Equal(2, results.Count(r => r.Outcome == AddCompanionOutcome.Invited));
        Assert.Equal(8, results.Count(r => r.Outcome == AddCompanionOutcome.Full));
        var (roster, _) = await db.GetBookingHunters(bookingId, _mainUserId, false);
        Assert.Equal(3, roster!.Hunters.Count);
    }
}
