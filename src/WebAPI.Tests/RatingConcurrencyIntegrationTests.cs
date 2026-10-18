using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves SetRoleRating's MERGE ... WITH (HOLDLOCK) actually serializes concurrent
/// submissions for the same trip instead of racing to the unique constraint - the bug
/// the old IF EXISTS/UPDATE/ELSE/INSERT version had (two near-simultaneous submissions
/// could both take the INSERT branch and one would crash on
/// UQ_ratings_booking_rater_ratee). This can only be proven against a real SQL Server,
/// not a Moq'd Rating_db, so unlike the rest of the suite this needs the same local
/// LocalDB instance the app itself runs against. It seeds and tears down its own
/// throwaway users/roles/booking - touches no real data.
///
/// Teardown temporarily disables ratingHistory's append-only triggers via direct DDL
/// to remove this test's own scaffold rows. That's a test-only escape hatch on this
/// test's own SQL connection - the application code path (Rating_db, AdminController)
/// never does this, so the immutability guarantee for real traffic is untouched.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class RatingConcurrencyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private int _hunterUserId;
    private int _phUserId;
    private int _bookingId;
    private bool _dbAvailable;

    public RatingConcurrencyIntegrationTests(WebApplicationFactory<Program> factory)
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

            // Two throwaway users (hunter + PH) with a single Completed booking linking them.
            using (var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('ConcurrencyTest', 'Hunter', CONCAT('concurrency.hunter.', NEWID(), '@test.local'), 'x', '0820000001', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _hunterUserId = (int)(await cmd.ExecuteScalarAsync())!;
            }
            using (var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('ConcurrencyTest', 'Ph', CONCAT('concurrency.ph.', NEWID(), '@test.local'), 'x', '0820000002', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _phUserId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            int hunterRoleId = await GetOrCreateRoleId(conn, "HUNTER", "Jagter");
            int phRoleId = await GetOrCreateRoleId(conn, "PH", "Professionele Jagter");

            int hunterUserRoleId;
            using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
            {
                cmd.Parameters.AddWithValue("@u", _hunterUserId);
                cmd.Parameters.AddWithValue("@r", hunterRoleId);
                hunterUserRoleId = (int)(await cmd.ExecuteScalarAsync())!;
            }
            int phUserRoleId;
            using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
            {
                cmd.Parameters.AddWithValue("@u", _phUserId);
                cmd.Parameters.AddWithValue("@r", phRoleId);
                phUserRoleId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            int phId;
            using (var cmd = new SqlCommand("INSERT INTO ph (phUserRoleId, phBio) OUTPUT inserted.phId VALUES (@ur, 'Concurrency test PH')", conn))
            {
                cmd.Parameters.AddWithValue("@ur", phUserRoleId);
                phId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            int packageId;
            using (var cmd = new SqlCommand("SELECT TOP 1 packageId FROM packages", conn))
            {
                var result = await cmd.ExecuteScalarAsync();
                packageId = result == null ? 0 : (int)result;
            }

            using (var cmd = new SqlCommand(@"
                INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests, bookPhId)
                OUTPUT inserted.bookingId
                VALUES (@hunterUrId, @packageId, '2026-01-01', '2026-01-02', 'Completed', 1, 0, @phId)", conn))
            {
                cmd.Parameters.AddWithValue("@hunterUrId", hunterUserRoleId);
                cmd.Parameters.AddWithValue("@packageId", packageId);
                cmd.Parameters.AddWithValue("@phId", phId);
                _bookingId = (int)(await cmd.ExecuteScalarAsync())!;
            }
        }
        catch
        {
            // No local SQL Server reachable - this test class self-skips (see the Fact
            // below) rather than failing the whole suite on a machine without LocalDB.
            _dbAvailable = false;
        }
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
                // Keep going - one cleanup step failing (e.g. a table not existing yet
                // on an older schema) must not strand every step after it, leaving
                // this test's scaffold users/rows behind permanently.
            }
        }

        await Exec("ALTER TABLE ratingHistory DISABLE TRIGGER ALL");
        await Exec($"DELETE FROM ratingHistory WHERE bookingId = {_bookingId}");
        await Exec("ALTER TABLE ratingHistory ENABLE TRIGGER ALL");
        await Exec($"DELETE FROM ratingDisputes WHERE ratingId IN (SELECT ratingId FROM ratings WHERE bookingId = {_bookingId})");
        await Exec($"DELETE FROM flaggedRatingComments WHERE frcRatingId IN (SELECT ratingId FROM ratings WHERE bookingId = {_bookingId})");
        await Exec($"DELETE FROM ratings WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM bookings WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId = {_phUserId})");
        await Exec($"DELETE FROM userRoles WHERE urUserId IN ({_hunterUserId}, {_phUserId})");
        await Exec($"DELETE FROM users WHERE userId IN ({_hunterUserId}, {_phUserId})");
    }

    [Fact]
    public async Task SetRoleRating_WithConcurrentSubmissionsForSameTrip_ProducesExactlyOneRow()
    {
        if (!_dbAvailable)
        {
            // Self-skip: no local SQL Server available in this environment.
            return;
        }

        var ratingDb = new Rating_db(_connectionString);
        const int concurrentSubmissions = 20;

        var tasks = Enumerable.Range(1, concurrentSubmissions).Select(i => ratingDb.SetRoleRating(new SetRatingRequest
        {
            BookingId = _bookingId,
            RaterUserId = _hunterUserId,
            RateeUserId = _phUserId,
            RateeRoleCode = "PH",
            Stars = (i % 5) + 1,
            Comment = $"Concurrent submission #{i}"
        }));

        // The old IF EXISTS/UPDATE/ELSE/INSERT implementation would throw here under
        // load (unique constraint violation) instead of every task completing cleanly.
        var ratingIds = await Task.WhenAll(tasks);

        Assert.All(ratingIds, id => Assert.True(id > 0));

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var countCmd = new SqlCommand("SELECT COUNT(*) FROM ratings WHERE bookingId = @b AND raterUserId = @r AND rateeUserId = @e", conn);
        countCmd.Parameters.AddWithValue("@b", _bookingId);
        countCmd.Parameters.AddWithValue("@r", _hunterUserId);
        countCmd.Parameters.AddWithValue("@e", _phUserId);
        int rowCount = (int)(await countCmd.ExecuteScalarAsync())!;

        Assert.Equal(1, rowCount);
    }
}
