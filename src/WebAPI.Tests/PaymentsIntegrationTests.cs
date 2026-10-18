using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// End to end through the real HTTP pipeline (JWT, [Authorize], controllers) and a real
/// SQL Server: a hunter, PH or advertiser can no longer mark anything paid themselves;
/// the server prices the payment; only the (Development) simulator or a verified PayFast
/// notification credits it. Self-skips without LocalDB; removes everything it creates.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class PaymentsIntegrationTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly string _connectionString;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private readonly List<int> _userIds = new();
    private readonly List<int> _bookingIds = new();
    private readonly List<int> _adIds = new();
    private int _packageId;
    private bool _dbAvailable;

    public PaymentsIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _packageId = (int)(await Scalar("SELECT TOP 1 packageId FROM packages ORDER BY packageId"))!;
            _dbAvailable = true;
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
            try { using var cmd = new SqlCommand(sql, conn); await cmd.ExecuteNonQueryAsync(); }
            catch { }
        }
        string In(List<int> ids) => ids.Count == 0 ? "NULL" : string.Join(",", ids);
        string users = In(_userIds);
        await Exec($"DELETE FROM bookingAuditLogs WHERE bookingId IN ({In(_bookingIds)})");
        await Exec($"DELETE FROM bookings WHERE bookingId IN ({In(_bookingIds)})");
        await Exec($"DELETE FROM adPayments WHERE adId IN ({In(_adIds)})");
        await Exec($"DELETE FROM advertisements WHERE advertisementId IN ({In(_adIds)}) OR adTitle LIKE '%{_run}%'");
        await Exec($"DELETE FROM ap WHERE apUserRoleId IN (SELECT userRoleId FROM userRoles WHERE urUserId IN ({users}))");
        await Exec($"DELETE FROM roleSubscriptions WHERE subUserId IN ({users})");
        await Exec($"DELETE FROM userRoles WHERE urUserId IN ({users})");
        await Exec($"DELETE FROM users WHERE userId IN ({users})");
    }

    private async Task<object?> Scalar(string sql, params (string Name, object Value)[] args)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<(int UserId, Dictionary<string, int> Roles)> User(string name, params string[] roleCodes)
    {
        int userId = (int)(await Scalar(@"
            INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
            OUTPUT inserted.userId VALUES (@n, 'Betaal', @e, 'x', '0820000095', 1, 1, SYSUTCDATETIME(), 0)",
            ("@n", name), ("@e", $"{name.ToLowerInvariant()}.{_run}@payments.test")))!;
        _userIds.Add(userId);
        var roles = new Dictionary<string, int>();
        foreach (var code in roleCodes)
        {
            int roleId = (int)(await Scalar("SELECT TOP 1 roleId FROM roles WHERE roleCode = @c ORDER BY roleId", ("@c", code)))!;
            roles[code] = (int)(await Scalar("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", ("@u", userId), ("@r", roleId)))!;
        }
        return (userId, roles);
    }

    private async Task<int> Booking(int hunterUserRoleId, string status, decimal total)
    {
        int id = (int)(await Scalar(@"INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests, bookTotalPrice, bookPaymentStatus)
                                      OUTPUT inserted.bookingId VALUES (@h, @p, '2027-02-01', '2027-02-04', @s, 1, 0, @t, 'Pending')",
            ("@h", hunterUserRoleId), ("@p", _packageId), ("@s", status), ("@t", total)))!;
        _bookingIds.Add(id);
        return id;
    }

    private HttpClient Client(int userId, params string[] roles)
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Email, $"u{userId}@payments.test") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256Signature)
        }));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Status, string? PaymentStatus, string? Reference)> BookingState(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT bookStatus, bookPaymentStatus, bookPaymentReference FROM bookings WHERE bookingId = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
    }

    private const string ReturnUrl = "https://localhost:7285/payment?status=success";
    private const string CancelUrl = "https://localhost:7285/jagter/bookings";

    [Fact]
    public async Task A_hunter_cannot_mark_their_own_booking_paid_any_more()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("SelfPay", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Approved", 10000m);
        var client = Client(hunter, "HUNTER");

        var setPaid = await client.PostAsJsonAsync($"api/bookings/setPaymentStatus/{booking}", "Paid");
        var update = await client.PostAsJsonAsync($"api/bookings/updateBooking/{booking}", new { PaymentStatus = "Paid", Status = "Paid", TotalPrice = 1 });
        var approve = await client.PostAsJsonAsync($"api/bookings/approveBooking/{booking}", "Paid");

        Assert.Equal(HttpStatusCode.Forbidden, setPaid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        var state = await BookingState(booking);
        Assert.Equal("Approved", state.Status);
        Assert.Equal("Pending", state.PaymentStatus);
    }

    [Fact]
    public async Task A_hunter_cannot_approve_their_own_pending_booking()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("SelfApprove", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Pending", 5000m);

        var res = await Client(hunter, "HUNTER").PostAsJsonAsync($"api/bookings/approveBooking/{booking}", "Approved");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("Pending", (await BookingState(booking)).Status);
    }

    [Fact]
    public async Task A_hunter_can_cancel_their_request_and_it_reads_as_cancelled_not_rejected()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("Cancel", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Pending", 5000m);

        var res = await Client(hunter, "HUNTER").PostAsJsonAsync($"api/bookings/approveBooking/{booking}", "Cancelled");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Cancelled", (await BookingState(booking)).Status);
    }

    [Fact]
    public async Task Checkout_is_priced_by_the_server_and_simulating_it_marks_the_booking_paid_once()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("Payer", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Approved", 10000m);
        var client = Client(hunter, "HUNTER");
        decimal pct = Convert.ToDecimal(await Scalar("SELECT TRY_CAST(settingValue AS DECIMAL(9,2)) FROM systemSettings WHERE settingKey = 'BookingDepositPercentage'") ?? 100m);

        var checkout = await client.PostAsJsonAsync("api/payments/checkout", new { kind = "booking", id = booking, returnUrl = ReturnUrl, cancelUrl = CancelUrl });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var form = await checkout.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Math.Round(10000m * pct / 100m, 2), form.GetProperty("amount").GetDecimal());
        var fields = form.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("value").GetString()!);
        Assert.Equal(booking.ToString(), fields["custom_int1"]);
        Assert.Equal(hunter.ToString(), fields["custom_int3"]);
        Assert.True(fields.ContainsKey("signature"));
        Assert.Equal("Pending", (await BookingState(booking)).PaymentStatus);   // starting a checkout pays nothing

        var first = await client.PostAsJsonAsync("api/payments/simulate", new { kind = "booking", id = booking });
        var second = await client.PostAsJsonAsync("api/payments/simulate", new { kind = "booking", id = booking });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyPaid").GetBoolean());
        Assert.True((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyPaid").GetBoolean());
        var state = await BookingState(booking);
        Assert.Equal("Paid", state.Status);
        Assert.Equal("Paid", state.PaymentStatus);
        Assert.StartsWith("PF-SIM-", state.Reference);
        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM bookingAuditLogs WHERE bookingId = @b AND actionType = 'Betaling Ontvang'", ("@b", booking)))!);
    }

    [Fact]
    public async Task Nobody_else_can_start_or_simulate_payment_for_a_booking()
    {
        if (!_dbAvailable) return;
        var (_, roles) = await User("Owner", "HUNTER");
        var (other, _) = await User("Other", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Approved", 10000m);
        var client = Client(other, "HUNTER");

        var checkout = await client.PostAsJsonAsync("api/payments/checkout", new { kind = "booking", id = booking, returnUrl = ReturnUrl, cancelUrl = CancelUrl });
        var simulate = await client.PostAsJsonAsync("api/payments/simulate", new { kind = "booking", id = booking });

        Assert.Equal(HttpStatusCode.NotFound, checkout.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, simulate.StatusCode);
        Assert.Equal("Pending", (await BookingState(booking)).PaymentStatus);
    }

    [Fact]
    public async Task A_pending_booking_cannot_be_paid_yet()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("Early", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Pending", 10000m);

        var res = await Client(hunter, "HUNTER").PostAsJsonAsync("api/payments/simulate", new { kind = "booking", id = booking });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Pending", (await BookingState(booking)).Status);
    }

    [Fact]
    public async Task A_forged_PayFast_notification_does_not_mark_anything_paid()
    {
        if (!_dbAvailable) return;
        var (hunter, roles) = await User("Forged", "HUNTER");
        int booking = await Booking(roles["HUNTER"], "Approved", 10000m);
        var body = $"m_payment_id=BOOKING-{booking}-x&pf_payment_id=123&payment_status=COMPLETE&amount_gross=1.00&custom_str1=BOOKING&custom_int1={booking}&custom_int3={hunter}&merchant_id=10000100&signature=00000000000000000000000000000000";

        var res = await _factory.CreateClient().PostAsync("api/payfast/itn", new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);   // PayFast must get a 200 so it stops resending
        Assert.Equal("Pending", (await BookingState(booking)).PaymentStatus);
    }

    [Fact]
    public async Task A_PH_cannot_activate_their_listing_without_paying()
    {
        if (!_dbAvailable) return;
        var (ph, _) = await User("FreeRider", "PH");
        var client = Client(ph, "PH");

        var save = await client.PostAsJsonAsync($"api/profile/subscription/{ph}/PH",
            new { UserId = ph, RoleCode = "PH", IsActive = true, ExpiryDate = DateTime.UtcNow.AddYears(1), PaymentStatus = "Paid", ActiveDurationMonths = 12 });
        var credit = await client.PostAsJsonAsync("api/profile/subscription/processPayment",
            new { UserId = ph, RoleCode = "PH", DurationMonths = 12, Amount = 1, PaymentReference = "SELF" });

        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, credit.StatusCode);
        Assert.Equal(0, (int)(await Scalar("SELECT COUNT(*) FROM roleSubscriptions WHERE subUserId = @u AND subPaymentStatus = 'Paid'", ("@u", ph)))!);
    }

    [Fact]
    public async Task A_paid_PH_listing_can_be_paused_and_resumed_but_not_extended_by_hand()
    {
        if (!_dbAvailable) return;
        var (ph, _) = await User("PausePh", "PH");
        var client = Client(ph, "PH");

        var paid = await client.PostAsJsonAsync("api/payments/simulate", new { kind = "subscription", roleCode = "PH", months = 1 });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var expiry = (DateTime)(await Scalar("SELECT subExpiryDate FROM roleSubscriptions WHERE subUserId = @u AND subRoleCode = 'PH'", ("@u", ph)))!;
        Assert.InRange(expiry, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));

        var pause = await client.PostAsJsonAsync($"api/profile/subscription/{ph}/PH", new { IsActive = false, ExpiryDate = DateTime.UtcNow.AddYears(5) });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        Assert.False((bool)(await Scalar("SELECT subIsActive FROM roleSubscriptions WHERE subUserId = @u AND subRoleCode = 'PH'", ("@u", ph)))!);

        var resume = await client.PostAsJsonAsync($"api/profile/subscription/{ph}/PH", new { IsActive = true, ExpiryDate = DateTime.UtcNow.AddYears(5) });
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        Assert.True((bool)(await Scalar("SELECT subIsActive FROM roleSubscriptions WHERE subUserId = @u AND subRoleCode = 'PH'", ("@u", ph)))!);
        Assert.Equal(expiry, (DateTime)(await Scalar("SELECT subExpiryDate FROM roleSubscriptions WHERE subUserId = @u AND subRoleCode = 'PH'", ("@u", ph)))!);
    }

    [Fact]
    public async Task A_subscription_for_a_role_you_do_not_hold_is_refused()
    {
        if (!_dbAvailable) return;
        var (hunter, _) = await User("NotPj", "HUNTER");
        var res = await Client(hunter, "HUNTER").PostAsJsonAsync("api/payments/simulate", new { kind = "subscription", roleCode = "PJ", months = 12 });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task An_advertiser_cannot_make_an_ad_live_without_paying()
    {
        if (!_dbAvailable) return;
        var (apUser, roles) = await User("Adverteerder", "AP");
        await Scalar("INSERT INTO ap (apUserRoleId) VALUES (@r); SELECT 1", ("@r", roles["AP"]));
        var client = Client(apUser, "AP");

        var create = await client.PostAsJsonAsync("api/Ads/createAd", new
        {
            Title = $"Gratis advertensie {_run}", Status = "Aktief", ExpiryDate = DateTime.Now.AddYears(1),
            PaymentRef = "SELF", LastPaymentAmount = 1, LastPaymentMonths = 12, Price = 5000
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        int adId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        _adIds.Add(adId);

        var update = await client.PostAsJsonAsync($"api/Ads/updateAd/{adId}", new { Status = "Aktief", ExpiryDate = DateTime.Now.AddYears(1), PaymentRef = "SELF2", Title = $"Nuwe titel {_run}" });
        var record = await client.PostAsJsonAsync($"api/Ads/recordPayment/{adId}", new { paymentRef = "SELF3", months = 12, amount = 1 });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);         // content edits still work...
        Assert.Equal(HttpStatusCode.Forbidden, record.StatusCode);
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT adStatus, adExpirationTimestamp, adPaymentRef, adTitle, (SELECT COUNT(*) FROM adPayments WHERE adId = @id) FROM advertisements WHERE advertisementId = @id", conn);
        cmd.Parameters.AddWithValue("@id", adId);
        using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal("Pending Payment", r.GetString(0));                // ...but it is not live
        Assert.True(r.IsDBNull(1));
        Assert.True(r.IsDBNull(2));
        Assert.Equal($"Nuwe titel {_run}", r.GetString(3));
        Assert.Equal(0, r.GetInt32(4));
    }

    [Fact]
    public async Task A_non_advertiser_cannot_post_an_ad_or_gain_the_AP_role_by_trying()
    {
        if (!_dbAvailable) return;
        var (hunter, _) = await User("NotAp", "HUNTER");

        var res = await Client(hunter, "HUNTER").PostAsJsonAsync("api/Ads/createAd", new { Title = $"Kaping {_run}" });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM userRoles WHERE urUserId = @u", ("@u", hunter)))!);
        Assert.Equal(0, (int)(await Scalar("SELECT COUNT(*) FROM advertisements WHERE adTitle = @t", ("@t", $"Kaping {_run}")))!);
    }

    [Fact]
    public async Task An_advertisers_first_ad_creates_their_AP_record()
    {
        if (!_dbAvailable) return;
        var (apUser, roles) = await User("NuweAp", "AP");

        var res = await Client(apUser, "AP").PostAsJsonAsync("api/Ads/createAd", new { Title = $"Eerste advertensie {_run}" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        _adIds.Add((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM ap WHERE apUserRoleId = @r", ("@r", roles["AP"])))!);
    }

    [Fact]
    public async Task Paying_for_an_ad_activates_it_and_a_repeated_notification_does_not_extend_it_again()
    {
        if (!_dbAvailable) return;
        var (apUser, roles) = await User("BetaalAd", "AP");
        await Scalar("INSERT INTO ap (apUserRoleId) VALUES (@r); SELECT 1", ("@r", roles["AP"]));
        var client = Client(apUser, "AP");
        var create = await client.PostAsJsonAsync("api/Ads/createAd", new { Title = $"Betaalde advertensie {_run}" });
        int adId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        _adIds.Add(adId);

        var pay = await client.PostAsJsonAsync("api/payments/simulate", new { kind = "ad", id = adId, months = 3 });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        string reference = (await pay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetString()!;

        var ads = new Ads_db(_connectionString, new Address_db(_connectionString));
        var ad = (await ads.GetAd(adId))!;
        Assert.Equal("Aktief", ad.Status);
        Assert.InRange(ad.ExpiryDate!.Value, DateTime.Now.AddMonths(3).AddDays(-1), DateTime.Now.AddMonths(3).AddDays(1));
        decimal expected = Math.Round(3 * Convert.ToDecimal(await Scalar("SELECT TRY_CAST(settingValue AS DECIMAL(9,2)) FROM systemSettings WHERE settingKey = 'AdMonthlyPrice'") ?? 100m) * 0.95m, 2);
        Assert.Equal(expected, ad.LastPaymentAmount);

        // PayFast resends: the same reference must not add a second payment or more months.
        Assert.True(await ads.RecordPayment(adId, new RecordPaymentRequest { PaymentRef = reference, Months = 3, Amount = expected, PeriodStartDate = ad.ExpiryDate, PeriodEndDate = ad.ExpiryDate.Value.AddMonths(3) }));
        Assert.Equal(1, (int)(await Scalar("SELECT COUNT(*) FROM adPayments WHERE adId = @a", ("@a", adId)))!);
        Assert.Equal(ad.ExpiryDate, (await ads.GetAd(adId))!.ExpiryDate);

        // Pausing and resuming a paid ad is the advertiser's own choice.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"api/Ads/updateAd/{adId}", new { Status = "Onaktief" })).StatusCode);
        Assert.Equal("Onaktief", (await ads.GetAd(adId))!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"api/Ads/updateAd/{adId}", new { Status = "Aktief" })).StatusCode);
        Assert.Equal("Aktief", (await ads.GetAd(adId))!.Status);
    }
}
