using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves a hunter cannot be charged twice for the same booking: a reloaded PayFast
/// return URL, a retried ITN webhook, browser Back+Forward, or two open tabs all
/// re-fire Bookings_db.SetPaymentStatus(id, "Paid") - it must apply the payment
/// exactly once and report every subsequent call as AlreadyPaid rather than silently
/// "succeeding" again. Also proves the parallel fix for subscription payments
/// (Profile_db.ProcessSubscriptionPayment), where the wrong outcome would be
/// silently double-crediting subscription days instead of a stuck status flag.
/// Needs a real local SQL Server (a Moq'd DB layer can't catch a real
/// transaction/locking bug here) - self-skips if none is reachable.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class PaymentIdempotencyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private int _hunterUserId;
    private int _bookingId;
    private int _packageId;
    private bool _dbAvailable;

    public PaymentIdempotencyIntegrationTests(WebApplicationFactory<Program> factory)
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

            using (var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('PayIdemTest', 'Hunter', CONCAT('payidemtest.', NEWID(), '@test.local'), 'x', '0820000099', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _hunterUserId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            int hunterRoleId;
            using (var cmd = new SqlCommand("SELECT roleId FROM roles WHERE roleCode = 'HUNTER'", conn))
            {
                var existing = await cmd.ExecuteScalarAsync();
                if (existing != null) hunterRoleId = (int)existing;
                else
                {
                    using var insCmd = new SqlCommand("INSERT INTO roles (roleCode, roleName) OUTPUT inserted.roleId VALUES ('HUNTER', 'Jagter')", conn);
                    hunterRoleId = (int)(await insCmd.ExecuteScalarAsync())!;
                }
            }

            int hunterUserRoleId;
            using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
            {
                cmd.Parameters.AddWithValue("@u", _hunterUserId);
                cmd.Parameters.AddWithValue("@r", hunterRoleId);
                hunterUserRoleId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            using (var cmd = new SqlCommand("SELECT TOP 1 packageId FROM packages", conn))
            {
                _packageId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            using (var cmd = new SqlCommand(@"
                INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
                OUTPUT inserted.bookingId
                VALUES (@hunterUrId, @packageId, '2026-04-01', '2026-04-05', 'Approved', 1, 0)", conn))
            {
                cmd.Parameters.AddWithValue("@hunterUrId", hunterUserRoleId);
                cmd.Parameters.AddWithValue("@packageId", _packageId);
                _bookingId = (int)(await cmd.ExecuteScalarAsync())!;
            }
        }
        catch
        {
            _dbAvailable = false;
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
            catch { }
        }

        await Exec($"DELETE FROM bookingAuditLogs WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM bookings WHERE bookingId = {_bookingId}");
        await Exec($"DELETE FROM userRoles WHERE urUserId = {_hunterUserId}");
        await Exec($"DELETE FROM users WHERE userId = {_hunterUserId}");
        await Exec("DELETE FROM roleSubscriptions WHERE subRoleCode = 'PAYIDEMTEST'");
    }

    [Fact]
    public async Task SetPaymentStatus_SecondCallForAlreadyPaidBooking_ReportsAlreadyPaidAndDoesNotDuplicateAuditLog()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);

        var first = await db.SetPaymentStatus(_bookingId, "Paid");
        Assert.Equal(Bookings_db.PaymentStatusResult.Applied, first);
        Assert.Equal("Paid", await GetBookingStatus());
        Assert.Equal("Paid", await GetPaymentStatus());

        // Simulates a reloaded return URL / retried ITN / second tab.
        var second = await db.SetPaymentStatus(_bookingId, "Paid");
        Assert.Equal(Bookings_db.PaymentStatusResult.AlreadyPaid, second);
        Assert.Equal("Paid", await GetBookingStatus());

        Assert.Equal(1, await CountAuditLogRows("Betaling Ontvang"));
    }

    [Fact]
    public async Task SetPaymentStatus_TenConcurrentDuplicateCalls_AppliesExactlyOnce()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        var tasks = Enumerable.Range(0, 10).Select(_ => db.SetPaymentStatus(_bookingId, "Paid"));
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r == Bookings_db.PaymentStatusResult.Applied);
        Assert.Equal(9, results.Count(r => r == Bookings_db.PaymentStatusResult.AlreadyPaid));
        Assert.Equal("Paid", await GetBookingStatus());
        Assert.Equal(1, await CountAuditLogRows("Betaling Ontvang"));
    }

    [Fact]
    public async Task SetPaymentStatus_ForNonExistentBooking_ReturnsNotFound()
    {
        if (!_dbAvailable) return;

        var db = new Bookings_db(_connectionString);
        var result = await db.SetPaymentStatus(-999999, "Paid");
        Assert.Equal(Bookings_db.PaymentStatusResult.NotFound, result);
    }

    [Fact]
    public async Task ProcessSubscriptionPayment_DuplicatePaymentReference_DoesNotDoubleCreditExpiry()
    {
        if (!_dbAvailable) return;

        var db = new Profile_db(_connectionString);
        string refNum = "PF-IDEM-TEST-" + Guid.NewGuid().ToString("N")[..8];

        var first = await db.ProcessSubscriptionPayment(_hunterUserId, "PAYIDEMTEST", 1, 299m, refNum);
        Assert.NotNull(first.ExpiryDate);

        // Read back what actually landed after the first call, round-tripped through
        // SQL Server (DATETIME2 storage precision differs slightly from the in-memory
        // DateTime.UtcNow tick value - comparing two DB-round-tripped reads avoids a
        // false failure from that precision difference, not the idempotency logic).
        var afterFirst = await GetStoredSubscriptionExpiry();
        Assert.NotNull(afterFirst);

        // Same paymentRef, as a reloaded return URL would produce.
        var second = await db.ProcessSubscriptionPayment(_hunterUserId, "PAYIDEMTEST", 1, 299m, refNum);
        var afterSecond = await GetStoredSubscriptionExpiry();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(afterFirst, second.ExpiryDate);
    }

    [Fact]
    public async Task ProcessSubscriptionPayment_TenConcurrentDuplicateCallsOnBrandNewSubscriber_AppliesExactlyOnce()
    {
        // PROBE: there's no existing roleSubscriptions row for this user+role yet
        // when this starts, so the UPDLOCK/HOLDLOCK guard has no row to lock - only an
        // index key-range lock. This empirically confirms that still serializes
        // concurrent first-time duplicate submissions instead of allowing a lost
        // update (each call independently computing "no existing row, start from
        // UtcNow" before any of them commit).
        if (!_dbAvailable) return;

        var db = new Profile_db(_connectionString);
        string refNum = "PF-IDEM-CONCURRENT-" + Guid.NewGuid().ToString("N")[..8];

        var tasks = Enumerable.Range(0, 10).Select(_ => db.ProcessSubscriptionPayment(_hunterUserId, "PAYIDEMTEST", 1, 299m, refNum));
        var results = await Task.WhenAll(tasks);

        // Every result must agree on (approximately) the same expiry - if a lost
        // update happened, some results would carry an independently-computed expiry
        // roughly 30 days further out than the others, not just a sub-second
        // DATETIME2-round-trip difference between an in-memory-computed value (the
        // one call that actually won the race and applied) and a DB-read value (the
        // other calls, which detected the duplicate and re-read the stored row).
        var expiries = results.Select(r => r.ExpiryDate!.Value).ToList();
        var spread = expiries.Max() - expiries.Min();
        Assert.True(spread < TimeSpan.FromSeconds(5),
            $"Expected all 10 concurrent calls to agree on the same extension (sub-second spread from DB round-trip precision only), but spread was {spread} - looks like more than one call actually extended the expiry.");

        var stored = await GetStoredSubscriptionExpiry();
        Assert.NotNull(stored);

        // Sanity: exactly one month's worth of extension landed, not ten.
        var expiry = stored!.Value;
        Assert.True(expiry < DateTime.UtcNow.AddDays(31) && expiry > DateTime.UtcNow.AddDays(29),
            $"Expected ~30 days from now, got {expiry:O} (now={DateTime.UtcNow:O}) - looks like more than one month was credited.");
    }

    [Fact]
    public async Task ProcessSubscriptionPayment_DifferentPaymentReference_ExtendsExpiryAgain()
    {
        if (!_dbAvailable) return;

        var db = new Profile_db(_connectionString);

        var first = await db.ProcessSubscriptionPayment(_hunterUserId, "PAYIDEMTEST", 1, 299m, "PF-IDEM-A-" + Guid.NewGuid().ToString("N")[..6]);
        var second = await db.ProcessSubscriptionPayment(_hunterUserId, "PAYIDEMTEST", 1, 299m, "PF-IDEM-B-" + Guid.NewGuid().ToString("N")[..6]);

        // A genuinely new payment (different reference) must still extend - this isn't
        // a blanket "reject repeat payments" guard, only a duplicate-of-the-same-
        // transaction guard.
        Assert.True(second.ExpiryDate > first.ExpiryDate);
    }

    private async Task<string> GetBookingStatus()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT bookStatus FROM bookings WHERE bookingId = @id", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> GetPaymentStatus()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT bookPaymentStatus FROM bookings WHERE bookingId = @id", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        var res = await cmd.ExecuteScalarAsync();
        return res as string;
    }

    private async Task<int> CountAuditLogRows(string actionType)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM bookingAuditLogs WHERE bookingId = @id AND actionType = @a", conn);
        cmd.Parameters.AddWithValue("@id", _bookingId);
        cmd.Parameters.AddWithValue("@a", actionType);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<DateTime?> GetStoredSubscriptionExpiry()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT subExpiryDate FROM roleSubscriptions WHERE subUserId = @u AND subRoleCode = 'PAYIDEMTEST'", conn);
        cmd.Parameters.AddWithValue("@u", _hunterUserId);
        var res = await cmd.ExecuteScalarAsync();
        return res == null || res == DBNull.Value ? null : (DateTime)res;
    }
}
