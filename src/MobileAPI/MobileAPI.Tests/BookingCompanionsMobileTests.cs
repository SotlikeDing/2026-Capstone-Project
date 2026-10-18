using Microsoft.Data.SqlClient;
using MobileApi.Database;
using MobileApi.Models;

namespace MobileAPI.Tests;

/// <summary>
/// The app's booking list against the real local database: a hunter added to someone
/// else's confirmed booking sees it (as not-the-main-hunter, cannot manage), the main
/// hunter sees it as theirs (can manage while confirmed), and the mobile copy of the
/// roster rules behaves like the WebAPI original. Needs the bookingCompanions table
/// (WebAPI migration); self-skips without a reachable LocalDB, and removes its rows.
/// </summary>
public class BookingCompanionsMobileTests : IAsyncLifetime
{
    private const string ConnectionString = "Server=(localdb)\\Mikpunt;Database=Mikpunt;Integrated Security=True;MultipleActiveResultSets=True;Connect Timeout=5";
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private readonly List<int> _userIds = new();
    private int _bookingId;
    private int _mainUserId, _mainUserRoleId, _friendUserId, _friendUserRoleId;
    private bool _dbAvailable;

    private string Email(string who) => $"{who}.{_run}@mobilecompanions.test";

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            if (await Scalar(conn, "SELECT OBJECT_ID('dbo.bookingCompanions')") is DBNull or null) return;
            var packageId = await Scalar(conn, "SELECT TOP 1 packageId FROM packages ORDER BY packageId");
            if (packageId == null) return;
            var hunterRoleId = await Scalar(conn, "SELECT TOP 1 roleId FROM roles WHERE UPPER(roleCode) IN ('HUNTER', 'JAGTER') ORDER BY roleId");
            if (hunterRoleId == null) return;

            (_mainUserId, _mainUserRoleId) = await InsertHunter(conn, "Main", (int)hunterRoleId);
            (_friendUserId, _friendUserRoleId) = await InsertHunter(conn, "Friend", (int)hunterRoleId);

            using var cmd = new SqlCommand(@"
                INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
                OUTPUT inserted.bookingId
                VALUES (@h, @p, '2026-12-01', '2026-12-04', 'Paid', 2, 1)", conn);
            cmd.Parameters.AddWithValue("@h", _mainUserRoleId);
            cmd.Parameters.AddWithValue("@p", (int)packageId);
            _bookingId = (int)(await cmd.ExecuteScalarAsync())!;
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    private static async Task<object?> Scalar(SqlConnection conn, string sql)
    {
        using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<(int UserId, int UserRoleId)> InsertHunter(SqlConnection conn, string name, int hunterRoleId)
    {
        int userId;
        using (var cmd = new SqlCommand(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId
            VALUES (@n, 'MobileCompanionTest', @e, 'x', '0820000098', 1, 1, SYSUTCDATETIME(), 0)", conn))
        {
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@e", Email(name.ToLowerInvariant()));
            userId = (int)(await cmd.ExecuteScalarAsync())!;
        }
        _userIds.Add(userId);
        using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@r", hunterRoleId);
            return (userId, (int)(await cmd.ExecuteScalarAsync())!);
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            $"DELETE FROM bookingCompanions WHERE bookingId = {_bookingId}",
            $"DELETE FROM bookings WHERE bookingId = {_bookingId}",
            $"DELETE FROM userRoles WHERE urUserId IN ({string.Join(",", _userIds)})",
            $"DELETE FROM users WHERE userId IN ({string.Join(",", _userIds)})",
        })
        {
            try
            {
                using var cmd = new SqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }

    [Fact]
    public async Task An_added_hunter_sees_the_trip_in_the_app_and_only_the_main_hunter_manages_it()
    {
        if (!_dbAvailable) return;
        var companions = new MobileBookingCompanions_db(ConnectionString);
        var bookings = new MobileBookings_db(ConnectionString);

        Assert.DoesNotContain(await bookings.GetHunterBookings(_friendUserRoleId), b => b.Id == _bookingId);

        var added = await companions.AddCompanion(_bookingId, _mainUserId, isAdmin: false, "Friend", Email("friend"));
        Assert.Equal(AddCompanionOutcome.Linked, added.Outcome);
        Assert.Equal(AddCompanionOutcome.Full, (await companions.AddCompanion(_bookingId, _mainUserId, false, "Other", Email("other"))).Outcome);

        var friendView = Assert.Single(await bookings.GetHunterBookings(_friendUserRoleId), b => b.Id == _bookingId);
        Assert.False(friendView.IsMainHunter);
        Assert.False(friendView.CanManageHunters);
        Assert.Equal("Main MobileCompanionTest", friendView.HunterName);
        Assert.Equal("Betaal", friendView.Status); // Paid bookings have their own label (main_dev)

        var mainView = Assert.Single(await bookings.GetHunterBookings(_mainUserRoleId), b => b.Id == _bookingId);
        Assert.True(mainView.IsMainHunter);
        Assert.True(mainView.CanManageHunters);
        Assert.Equal(2, mainView.MaxHunters);
        Assert.Equal(2, mainView.HunterCount);

        var (roster, onBooking) = await companions.GetBookingHunters(_bookingId, _friendUserId, isAdmin: false);
        Assert.True(onBooking);
        Assert.False(roster!.CanManage);

        Assert.Equal(RemoveCompanionOutcome.Removed, await companions.RemoveCompanion(_bookingId, added.Hunter!.CompanionId, _friendUserId, false));
        Assert.DoesNotContain(await bookings.GetHunterBookings(_friendUserRoleId), b => b.Id == _bookingId);
    }
}
