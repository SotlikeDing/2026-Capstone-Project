using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Regression tests (real SQL Server) for the High findings of Plans/e2e_audit_report.md:
/// creating a farm no longer grants the Farmer role; only a PJ can create a package; ad
/// row-count checks work (the schema batch used to fail on an NVARCHAR(MAX) index key and
/// leave SET NOCOUNT ON, so every update reported failure); a farmer's booking list carries
/// farm and price; the browse directory reports each profile's real listing status.
/// Self-skips without LocalDB; removes everything it creates.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class HighFixesIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private readonly List<int> _userIds = new();
    private readonly List<int> _farmIds = new();
    private readonly List<int> _packageIds = new();
    private readonly List<int> _bookingIds = new();
    private bool _dbAvailable;

    public HighFixesIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    private async Task<object?> Scalar(string sql, params (string Name, object Value)[] args)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<int> RoleId(string code) =>
        (int)(await Scalar("SELECT TOP 1 roleId FROM roles WHERE roleCode = @c ORDER BY roleId", ("@c", code)))!;

    /// <summary>A verified user holding the given roles; returns (userId, userRoleIds by role code).</summary>
    private async Task<(int UserId, Dictionary<string, int> Roles)> User(string name, params string[] roleCodes)
    {
        int userId = (int)(await Scalar(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId VALUES (@n, 'HighFix', @e, 'x', '0820000096', 1, 1, SYSUTCDATETIME(), 0)",
            ("@n", name), ("@e", $"{name.ToLowerInvariant()}.{_run}@highfix.test")))!;
        _userIds.Add(userId);
        var roles = new Dictionary<string, int>();
        foreach (var code in roleCodes)
        {
            roles[code] = (int)(await Scalar("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)",
                ("@u", userId), ("@r", await RoleId(code))))!;
        }
        return (userId, roles);
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
            catch { } // keep cleaning up
        }
        string In(List<int> ids) => ids.Count == 0 ? "NULL" : string.Join(",", ids);
        string users = In(_userIds);
        await Exec($"DELETE FROM bookings WHERE bookingId IN ({In(_bookingIds)})");
        await Exec($"DELETE FROM packages WHERE packageId IN ({In(_packageIds)}) OR pTitle LIKE '%{_run}%'");
        await Exec($"DELETE FROM farmLodge WHERE flFarmId IN ({In(_farmIds)})");
        await Exec($"DELETE FROM availableAnimals WHERE aaFarmId IN ({In(_farmIds)})");
        await Exec($"DELETE FROM farm WHERE farmId IN ({In(_farmIds)})");
        await Exec($"DELETE FROM roleSubscriptions WHERE subUserId IN ({users})");
        await Exec($"DELETE FROM farmer WHERE fUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId IN ({users}))");
        await Exec($"DELETE FROM pj WHERE pjUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId IN ({users}))");
        await Exec($"DELETE FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId IN ({users}))");
        await Exec($"DELETE FROM userRoles WHERE urUserId IN ({users})");
        await Exec($"DELETE FROM users WHERE userId IN ({users})");
    }

    [Fact]
    public async Task A_non_farmer_cannot_create_a_farm_and_gains_no_role()
    {
        if (!_dbAvailable) return;
        var (hunter, _) = await User("Hunter", "HUNTER");
        var db = new Farmer_db(_connectionString, new Address_db(_connectionString));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            db.CreateFarm(hunter, new CreateFarmRequest { Name = $"Gekaapte Plaas {_run}", Size = 10 }));

        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM userRoles WHERE urUserId = @u", ("@u", hunter)))!);
        Assert.Equal(0, (int)(await Scalar("SELECT COUNT(*) FROM farm WHERE farmName = @n", ("@n", $"Gekaapte Plaas {_run}")))!);
    }

    [Fact]
    public async Task A_farmer_can_create_a_farm()
    {
        if (!_dbAvailable) return;
        var (farmer, _) = await User("Farmer", "FARMER");
        var db = new Farmer_db(_connectionString, new Address_db(_connectionString));

        int farmId = await db.CreateFarm(farmer, new CreateFarmRequest { Name = $"Eie Plaas {_run}", Size = 50, DailyRateHunter = 1200 });
        _farmIds.Add(farmId);

        Assert.True(farmId > 0);
        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM userRoles WHERE urUserId = @u", ("@u", farmer)))!);
    }

    [Fact]
    public async Task Only_a_pj_can_create_a_package()
    {
        if (!_dbAvailable) return;
        var (hunter, _) = await User("NotPj", "HUNTER");
        var (pjUser, _) = await User("RealPj", "PJ");
        var db = new Package_db(_connectionString);
        CreatePackageRequest Req(int owner, string title) => new()
        {
            PTitle = title, PDescription = "toets", PStockPrice = 1000, PDurationDays = 3, PMaxParticipants = 2,
            PIncludedItems = "Koedoe", PStatus = "Active", PPjId = owner,
        };

        Assert.Equal(0, await db.CreatePacket(Req(hunter, $"Jagter pakket {_run}")));
        Assert.Equal(0, (int)(await Scalar("SELECT COUNT(*) FROM packages WHERE pTitle = @t", ("@t", $"Jagter pakket {_run}")))!);

        int id = await db.CreatePacket(Req(pjUser, $"PJ pakket {_run}"));
        _packageIds.Add(id);
        Assert.True(id > 0);
        Assert.NotNull(await Scalar("SELECT pPjId FROM packages WHERE packageId = @id AND pPjId IS NOT NULL", ("@id", id)));
    }

    [Fact]
    public async Task Ad_views_and_clicks_report_success_and_the_schema_check_completes()
    {
        if (!_dbAvailable) return;
        var adId = await Scalar("SELECT TOP 1 advertisementId FROM advertisements ORDER BY advertisementId");
        if (adId == null) return; // no ads in this database
        int id = (int)adId;
        var db = new Ads_db(_connectionString, new Address_db(_connectionString));
        int views = (int)(await Scalar("SELECT ISNULL(adViews, 0) FROM advertisements WHERE advertisementId = @id", ("@id", id)))!;
        int clicks = (int)(await Scalar("SELECT ISNULL(adClicks, 0) FROM advertisements WHERE advertisementId = @id", ("@id", id)))!;
        try
        {
            Assert.True(await db.RecordImpression(id));
            Assert.True(await db.RecordClick(id));
            Assert.Equal(views + 1, (int)(await Scalar("SELECT adViews FROM advertisements WHERE advertisementId = @id", ("@id", id)))!);
            Assert.NotNull(await Scalar("SELECT 1 FROM sys.indexes WHERE name = 'IX_ads_status_expiry' AND object_id = OBJECT_ID('advertisements')"));
        }
        finally
        {
            await Scalar("UPDATE advertisements SET adViews = @v, adClicks = @c WHERE advertisementId = @id; SELECT 1",
                ("@v", views), ("@c", clicks), ("@id", id));
        }
    }

    [Fact]
    public async Task A_farmers_booking_list_has_farm_price_and_payment_status()
    {
        if (!_dbAvailable) return;
        var (farmer, _) = await User("BookingFarmer", "FARMER");
        var (_, hunterRoles) = await User("BookingHunter", "HUNTER");
        var farmDb = new Farmer_db(_connectionString, new Address_db(_connectionString));
        int farmId = await farmDb.CreateFarm(farmer, new CreateFarmRequest { Name = $"Besprekingsplaas {_run}", Size = 10 });
        _farmIds.Add(farmId);
        int packageId = (int)(await Scalar(@"INSERT INTO packages (pCreatedTimestamp, pFarmId, pStockPrice, pTitle, pStatus)
                                              OUTPUT inserted.packageId VALUES (SYSUTCDATETIME(), @f, 1000, @t, 'Active')",
            ("@f", farmId), ("@t", $"Besprekingspakket {_run}")))!;
        _packageIds.Add(packageId);
        int bookingId = (int)(await Scalar(@"INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests, bookTotalPrice, bookPaymentStatus)
                                              OUTPUT inserted.bookingId VALUES (@h, @p, '2026-12-01', '2026-12-03', 'Paid', 2, 0, 7777, 'Paid')",
            ("@h", hunterRoles["HUNTER"]), ("@p", packageId)))!;
        _bookingIds.Add(bookingId);

        var list = await new Bookings_db(_connectionString).GetFarmBookings(farmer);

        var b = Assert.Single(list, x => x.Id == bookingId);
        Assert.Equal($"Besprekingsplaas {_run}", b.FarmName);
        Assert.Equal(farmId, b.FarmId);
        Assert.Equal(7777m, b.TotalAmount);
        Assert.Equal("Paid", b.PaymentStatus);
    }

    [Fact]
    public async Task The_directory_reports_each_profiles_real_listing_status()
    {
        if (!_dbAvailable) return;
        if (await Scalar("SELECT OBJECT_ID('dbo.roleSubscriptions')") is DBNull or null) return;
        var (paid, _) = await User("PaidPh", "PH");
        var (unpaid, _) = await User("UnpaidPh", "PH");
        var (lapsed, _) = await User("LapsedPh", "PH");
        await Scalar("INSERT INTO roleSubscriptions (subUserId, subRoleCode, subIsActive, subExpiryDate) VALUES (@u, 'PH', 1, DATEADD(day, 20, SYSUTCDATETIME())); SELECT 1", ("@u", paid));
        await Scalar("INSERT INTO roleSubscriptions (subUserId, subRoleCode, subIsActive, subExpiryDate) VALUES (@u, 'PH', 1, DATEADD(day, -1, SYSUTCDATETIME())); SELECT 1", ("@u", lapsed));

        var profiles = await new Team_db(_connectionString).BrowseByRoles(new List<string> { "PH" });

        Assert.True(profiles.Single(p => p.UserId == paid).IsSubscriptionActive);
        Assert.False(profiles.Single(p => p.UserId == unpaid).IsSubscriptionActive);
        Assert.False(profiles.Single(p => p.UserId == lapsed).IsSubscriptionActive);
    }
}
