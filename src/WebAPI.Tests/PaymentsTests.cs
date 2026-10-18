using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;
using WebAPI.Services.Payments;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// The payment-trust fix: amounts are priced on the server, a PayFast notification is only
/// credited after its signature, sender, PayFast's own confirmation and the amount check
/// out, and the simulator exists only in Development.
/// </summary>
public class PaymentsTests
{
    // ── PayFast signature ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc-_.XYZ09", "abc-_.XYZ09")]
    [InlineData("a b", "a+b")]
    [InlineData("~!*()'", "%7E%21%2A%28%29%27")]   // PHP urlencode encodes these; .NET's WebUtility doesn't
    [InlineData("Één", "%C3%89%C3%A9n")]
    [InlineData("https://x/y?a=1&b=2", "https%3A%2F%2Fx%2Fy%3Fa%3D1%26b%3D2")]
    public void UrlEncode_matches_PHP_urlencode(string input, string expected)
        => Assert.Equal(expected, PayFastSignature.UrlEncode(input));

    private static readonly List<KeyValuePair<string, string>> ReferenceFields = new()
    {
        new("merchant_id", "10000100"),
        new("merchant_key", "46f0cd694581a"),
        new("return_url", "https://localhost:7285/payment?status=success&bookingId=5"),
        new("amount", "4500.00"),
        new("item_name", "Kudu Jag ~ Één (5 dae)!"),
    };

    [Fact]
    public void Sign_matches_an_independently_computed_signature()
    {
        // Expected values computed separately (Python: md5 over the PHP-urlencoded string).
        Assert.Equal("9a06f1b6525284d17aa7086041b31561", PayFastSignature.Sign(ReferenceFields, null));
        Assert.Equal("95208f90f5cc1a81528ea3f84780e77f", PayFastSignature.Sign(ReferenceFields, "jt7NOE43FZPn"));
    }

    [Fact]
    public void Sign_skips_empty_fields_for_a_checkout_but_not_for_a_notification()
    {
        var withEmpty = new List<KeyValuePair<string, string>>(ReferenceFields) { new("name_last", "") };
        Assert.Equal(PayFastSignature.Sign(ReferenceFields, null), PayFastSignature.Sign(withEmpty, null));
        Assert.NotEqual(PayFastSignature.Sign(ReferenceFields, null), PayFastSignature.Sign(withEmpty, null, skipEmpty: false));
    }

    [Fact]
    public void ParseBody_keeps_field_order_and_decodes_values()
    {
        var fields = PayFastCheckout.ParseBody("b=2&a=Een+twee&c=%C3%89&empty=");
        Assert.Equal(new[] { "b", "a", "c", "empty" }, fields.Select(f => f.Key));
        Assert.Equal("Een twee", fields[1].Value);
        Assert.Equal("É", fields[2].Value);
        Assert.Equal("", fields[3].Value);
    }

    [Fact]
    public void BuildFields_signs_the_server_price_and_carries_the_item()
    {
        var options = new PayFastOptions { Passphrase = "geheim" };
        var quote = new PaymentQuote { Kind = PaymentKind.Booking, TargetId = 42, PayerUserId = 7, Amount = 1234.5m, ItemName = "Pakket", ItemDescription = "Deposito" };

        var fields = PayFastCheckout.BuildFields(options, quote, "https://localhost:7285/r", "https://localhost:7285/c", "https://api/itn", "Jan", "", "jan@x.test");
        string Get(string k) => fields.Single(f => f.Key == k).Value;

        Assert.Equal("1234.50", Get("amount"));
        Assert.Equal("BOOKING", Get("custom_str1"));
        Assert.Equal("42", Get("custom_int1"));
        Assert.Equal("7", Get("custom_int3"));
        Assert.DoesNotContain(fields, f => f.Key == "name_last");   // empty fields are left out
        Assert.Equal(PayFastSignature.Sign(fields, "geheim"), Get("signature"));
        Assert.StartsWith("BOOKING-42-", Get("m_payment_id"));
    }

    // ── PaymentService: prices only from the server ────────────────────────────

    private sealed class Settings : ISystemSettingsCache
    {
        private readonly Dictionary<string, decimal> _values;
        public Settings(Dictionary<string, decimal>? values = null) => _values = values ?? new();
        public Task RefreshAsync(SystemSettings_db settingsDb) => Task.CompletedTask;
        public void Set(string key, string value) => _values[key] = decimal.Parse(value, CultureInfo.InvariantCulture);
        public decimal GetDecimal(string key, decimal fallback) => _values.TryGetValue(key, out var v) ? v : fallback;
        public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? (int)v : fallback;
    }

    private sealed class Fakes
    {
        public Mock<Bookings_db> Bookings { get; } = new("x");
        public Mock<Profile_db> Profiles { get; } = new("x");
        public Mock<Ads_db> Ads { get; } = new("x", new Address_db("x"));
        public Mock<Payments_db> Payments { get; } = new("x");
        public Settings Settings { get; } = new(new() { ["BookingDepositPercentage"] = 30m, ["SubscriptionPrice_PH"] = 300m, ["AdMonthlyPrice"] = 100m });
        public PaymentService Service => new(Bookings.Object, Profiles.Object, Ads.Object, Payments.Object, Settings);
    }

    [Fact]
    public async Task Booking_deposit_is_the_booking_total_times_the_deposit_setting()
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = "Approved", TotalAmount = 10000m, PakketName = "Kudu",
            PriceBreakdownJson = "{\"Currency\":\"ZAR\"}" });

        var q = await f.Service.QuoteAsync(PaymentKind.Booking, 5, 7, null, 0);

        Assert.NotNull(q.Quote);
        Assert.Equal(3000m, q.Quote!.Amount);
        Assert.Equal("Kudu", q.Quote.ItemName);
    }

    [Theory]
    [InlineData("{\"Currency\":\"USD\"}")]
    [InlineData(null)]                                  // older bookings were priced from US$ package rates
    [InlineData("{\"package\": 45000, \"accommodation\": 7000}")]   // no currency + packages in US$
    public async Task A_US_dollar_quote_is_charged_in_rand_at_the_exchange_rate(string? breakdown)
    {
        var f = new Fakes();
        f.Settings.Set("UsdToZarRate", "18.50");
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = "Approved", TotalAmount = 1000m, PriceBreakdownJson = breakdown });

        var q = await f.Service.QuoteAsync(PaymentKind.Booking, 5, 7, null, 0);

        Assert.Equal(5550m, q.Quote!.Amount);                       // 1000 US$ x 18.50 x 30 % deposit
        Assert.Contains("US$ 1,000.00 teen R18.50/US$", q.Quote.ItemDescription);
    }

    [Theory]
    [InlineData("{\"Currency\":\"ZAR\"}", "ZAR")]
    [InlineData("{\"Currency\":\"zar\"}", "ZAR")]
    [InlineData("{\"Currency\":\"USD\"}", "USD")]
    [InlineData(null, "USD")]
    [InlineData("not json", "USD")]
    public void Booking_currency_comes_from_the_price_breakdown(string? breakdown, string expected)
        => Assert.Equal(expected, PaymentService.BookingCurrency(breakdown));

    [Theory]
    [InlineData(null)]
    [InlineData("{\"package\": 45000}")]
    public async Task With_package_prices_in_rand_a_quote_without_currency_is_charged_as_is(string? breakdown)
    {
        var f = new Fakes();
        f.Settings.Set("PackagePricesInZar", "1");
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = "Approved", TotalAmount = 10000m, PriceBreakdownJson = breakdown });

        var q = await f.Service.QuoteAsync(PaymentKind.Booking, 5, 7, null, 0);

        Assert.Equal(3000m, q.Quote!.Amount);   // R10 000 x 30 % deposit, no conversion
    }

    [Theory]
    [InlineData(8, "Approved", 404)]          // someone else's booking
    [InlineData(7, "Pending", 409)]           // not approved yet
    [InlineData(7, "PendingStaffAcceptance", 409)]
    [InlineData(7, "Rejected", 409)]
    public async Task Booking_deposit_is_refused_when_not_the_owners_approved_booking(int payer, string status, int expected)
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = status, TotalAmount = 10000m });

        var q = await f.Service.QuoteAsync(PaymentKind.Booking, 5, payer, null, 0);

        Assert.Null(q.Quote);
        Assert.Equal(expected, q.StatusCode);
    }

    [Fact]
    public async Task A_paid_booking_reports_already_paid()
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = "Paid", PaymentStatus = "Paid", TotalAmount = 100m });

        var q = await f.Service.QuoteAsync(PaymentKind.Booking, 5, 7, null, 0);

        Assert.True(q.AlreadyPaid);
        Assert.Null(q.Quote);
    }

    [Fact]
    public async Task A_booking_without_a_price_cannot_be_paid()
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 7, Status = "Approved", TotalAmount = 0m });
        Assert.Equal(409, (await f.Service.QuoteAsync(PaymentKind.Booking, 5, 7, null, 0)).StatusCode);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(3, 855)]      // 3 x 300 less 5%
    [InlineData(6, 1620)]     // 6 x 300 less 10%
    [InlineData(12, 3000)]    // 12 for the price of 10
    public async Task Subscription_price_follows_the_tiers(int months, decimal expected)
    {
        var f = new Fakes();
        f.Payments.Setup(d => d.UserHasRole(7, "PH")).ReturnsAsync(true);

        var q = await f.Service.QuoteAsync(PaymentKind.Subscription, 7, 7, "ph", months);

        Assert.Equal(expected, q.Quote!.Amount);
        Assert.Equal("PH", q.Quote.RoleCode);
    }

    [Fact]
    public async Task Boer_subscription_needs_the_FARMER_role()
    {
        var f = new Fakes();
        f.Payments.Setup(d => d.UserHasRole(7, "FARMER")).ReturnsAsync(false);
        Assert.Equal(403, (await f.Service.QuoteAsync(PaymentKind.Subscription, 7, 7, "Boer", 1)).StatusCode);
    }

    [Theory]
    [InlineData("PH", 2, 400)]       // not an offered term
    [InlineData("HUNTER", 1, 400)]   // not a listing role
    [InlineData(null, 1, 400)]
    public async Task Subscription_rejects_unknown_terms_and_roles(string? role, int months, int expected)
    {
        var f = new Fakes();
        f.Payments.Setup(d => d.UserHasRole(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);
        Assert.Equal(expected, (await f.Service.QuoteAsync(PaymentKind.Subscription, 7, 7, role, months)).StatusCode);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(3, 285)]
    [InlineData(6, 540)]
    [InlineData(12, 960)]
    public async Task Ad_price_follows_the_tiers(int months, decimal expected)
    {
        var f = new Fakes();
        f.Ads.Setup(d => d.GetAd(9)).ReturnsAsync(new AdResponse { Id = 9, UserId = 7, Title = "Gewere" });
        Assert.Equal(expected, (await f.Service.QuoteAsync(PaymentKind.Ad, 9, 7, null, months)).Quote!.Amount);
    }

    [Theory]
    [InlineData(8, 1, 404)]   // someone else's ad
    [InlineData(7, 0, 400)]
    [InlineData(7, 37, 400)]
    public async Task Ad_payment_is_refused_for_other_peoples_ads_and_bad_terms(int payer, int months, int expected)
    {
        var f = new Fakes();
        f.Ads.Setup(d => d.GetAd(9)).ReturnsAsync(new AdResponse { Id = 9, UserId = 7 });
        Assert.Equal(expected, (await f.Service.QuoteAsync(PaymentKind.Ad, 9, payer, null, months)).StatusCode);
    }

    [Fact]
    public async Task Applying_an_ad_payment_extends_a_running_ad_from_its_end_date()
    {
        var f = new Fakes();
        var end = DateTime.Now.AddDays(10);
        f.Ads.Setup(d => d.GetAd(9)).ReturnsAsync(new AdResponse { Id = 9, UserId = 7, ExpiryDate = end, PaymentRef = "PF-OLD" });
        RecordPaymentRequest? recorded = null;
        f.Ads.Setup(d => d.RecordPayment(9, It.IsAny<RecordPaymentRequest>(), null))
             .Callback<int, RecordPaymentRequest, int?>((_, r, _) => recorded = r).ReturnsAsync(true);

        var result = await f.Service.ApplyAsync(new PaymentQuote { Kind = PaymentKind.Ad, TargetId = 9, PayerUserId = 7, Months = 2, Amount = 200m }, "PF-NEW", "PayFast");

        Assert.Equal(PaymentApplyResult.Applied, result);
        Assert.Equal(end, recorded!.PeriodStartDate);
        Assert.Equal(end.AddMonths(2), recorded.PeriodEndDate);
        Assert.Equal(200m, recorded.Amount);
    }

    [Fact]
    public async Task Applying_the_same_ad_payment_twice_reports_already_paid()
    {
        var f = new Fakes();
        f.Ads.Setup(d => d.GetAd(9)).ReturnsAsync(new AdResponse { Id = 9, UserId = 7, PaymentRef = "PF-1" });

        var result = await f.Service.ApplyAsync(new PaymentQuote { Kind = PaymentKind.Ad, TargetId = 9, PayerUserId = 7, Months = 1, Amount = 100m }, "PF-1", "PayFast");

        Assert.Equal(PaymentApplyResult.AlreadyPaid, result);
        f.Ads.Verify(d => d.RecordPayment(It.IsAny<int>(), It.IsAny<RecordPaymentRequest>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task Applying_a_booking_payment_stores_the_reference()
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.SetPaymentStatus(5, "Paid", "PF-77")).ReturnsAsync(Bookings_db.PaymentStatusResult.Applied);

        var result = await f.Service.ApplyAsync(new PaymentQuote { Kind = PaymentKind.Booking, TargetId = 5, PayerUserId = 7, Amount = 1m }, "PF-77", "PayFast");

        Assert.Equal(PaymentApplyResult.Applied, result);
        f.Bookings.Verify(d => d.SetPaymentStatus(5, "Paid", "PF-77"), Times.Once);
    }

    // ── PayFast notification (ITN) ─────────────────────────────────────────────

    /// <summary>The ITN service with PayFast's network checks replaced by fixed answers.</summary>
    private sealed class TestItnService : PayFastItnService
    {
        public bool FromPayFast { get; set; } = true;
        public bool? Confirmed { get; set; } = true;
        public string? ConfirmedParamString { get; private set; }

        public TestItnService(PayFastOptions options, PaymentService payments)
            : base(Options.Create(options), new Mock<IHttpClientFactory>().Object, payments, NullLogger<PayFastItnService>.Instance) { }

        protected override Task<bool> IsFromPayFastAsync(IPAddress? remoteIp) => Task.FromResult(FromPayFast);
        protected override Task<bool?> ConfirmWithPayFastAsync(string paramString)
        {
            ConfirmedParamString = paramString;
            return Task.FromResult(Confirmed);
        }
    }

    private const string Passphrase = "toets-wagwoord";

    /// <summary>A notification as PayFast would post it for booking 5 (payer 7), signed with the passphrase.</summary>
    private static string ItnBody(string amount = "3000.00", string status = "COMPLETE", string merchant = "10000100", Action<List<KeyValuePair<string, string>>>? tamper = null, string? passphrase = Passphrase)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("m_payment_id", "BOOKING-5-abc"),
            new("pf_payment_id", "1089250"),
            new("payment_status", status),
            new("item_name", "Kudu"),
            new("item_description", ""),
            new("amount_gross", amount),
            new("amount_fee", "-69.00"),
            new("amount_net", "2931.00"),
            new("custom_str1", "BOOKING"),
            new("custom_str2", ""),
            new("custom_int1", "5"),
            new("custom_int2", "0"),
            new("custom_int3", "7"),
            new("name_first", "Jan"),
            new("email_address", "jan@x.test"),
            new("merchant_id", merchant),
        };
        fields.Add(new("signature", PayFastSignature.Sign(fields, passphrase, skipEmpty: false)));
        tamper?.Invoke(fields);
        return string.Join("&", fields.Select(f => $"{f.Key}={PayFastSignature.UrlEncode(f.Value)}"));
    }

    private static (TestItnService Itn, Mock<PaymentService> Payments) NewItn()
    {
        var f = new Fakes();
        var payments = new Mock<PaymentService>(f.Bookings.Object, f.Profiles.Object, f.Ads.Object, f.Payments.Object, f.Settings);
        payments.Setup(p => p.QuoteAsync(PaymentKind.Booking, 5, 7, It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(QuoteResult.Ok(new PaymentQuote { Kind = PaymentKind.Booking, TargetId = 5, PayerUserId = 7, Amount = 3000m }));
        payments.Setup(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(PaymentApplyResult.Applied);
        var itn = new TestItnService(new PayFastOptions { Passphrase = Passphrase }, payments.Object);
        return (itn, payments);
    }

    [Fact]
    public async Task A_genuine_notification_is_credited_with_PayFasts_reference()
    {
        var (itn, payments) = NewItn();

        var result = await itn.ProcessAsync(ItnBody(), IPAddress.Loopback);

        Assert.Equal(ItnOutcome.Applied, result.Outcome);
        payments.Verify(p => p.ApplyAsync(It.Is<PaymentQuote>(q => q.TargetId == 5 && q.Amount == 3000m), "1089250", "PayFast"), Times.Once);
        Assert.DoesNotContain("signature=", itn.ConfirmedParamString);
        Assert.Contains("item_description=&", itn.ConfirmedParamString);   // PayFast validates the exact posted fields
    }

    [Fact]
    public async Task A_forged_notification_is_rejected_and_nothing_is_credited()
    {
        var (itn, payments) = NewItn();
        // An attacker who doesn't know the passphrase signs without it.
        var result = await itn.ProcessAsync(ItnBody(passphrase: null), IPAddress.Loopback);

        Assert.Equal(ItnOutcome.Rejected, result.Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_notification_changed_after_signing_is_rejected()
    {
        var (itn, payments) = NewItn();
        var body = ItnBody(tamper: f => f[f.FindIndex(x => x.Key == "custom_int1")] = new("custom_int1", "6"));

        Assert.Equal(ItnOutcome.Rejected, (await itn.ProcessAsync(body, IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_notification_for_less_than_the_server_price_is_rejected()
    {
        var (itn, payments) = NewItn();
        Assert.Equal(ItnOutcome.Rejected, (await itn.ProcessAsync(ItnBody(amount: "1.00"), IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_notification_for_another_merchant_is_rejected()
    {
        var (itn, _) = NewItn();
        Assert.Equal(ItnOutcome.Rejected, (await itn.ProcessAsync(ItnBody(merchant: "99999"), IPAddress.Loopback)).Outcome);
    }

    [Fact]
    public async Task A_notification_from_outside_PayFast_is_rejected()
    {
        var (itn, payments) = NewItn();
        itn.FromPayFast = false;
        Assert.Equal(ItnOutcome.Rejected, (await itn.ProcessAsync(ItnBody(), IPAddress.Parse("203.0.113.9"))).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_notification_PayFast_does_not_confirm_is_rejected()
    {
        var (itn, payments) = NewItn();
        itn.Confirmed = false;
        Assert.Equal(ItnOutcome.Rejected, (await itn.ProcessAsync(ItnBody(), IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task When_PayFast_cannot_be_reached_the_notification_is_retried_later()
    {
        var (itn, payments) = NewItn();
        itn.Confirmed = null;
        Assert.Equal(ItnOutcome.RetryLater, (await itn.ProcessAsync(ItnBody(), IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("PENDING")]
    public async Task An_incomplete_payment_is_ignored(string status)
    {
        var (itn, payments) = NewItn();
        Assert.Equal(ItnOutcome.Ignored, (await itn.ProcessAsync(ItnBody(status: status), IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_repeated_notification_for_a_paid_booking_is_a_no_op()
    {
        var (itn, payments) = NewItn();
        payments.Setup(p => p.QuoteAsync(PaymentKind.Booking, 5, 7, It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(QuoteResult.Fail(409, "betaal", alreadyPaid: true));

        Assert.Equal(ItnOutcome.AlreadyPaid, (await itn.ProcessAsync(ItnBody(), IPAddress.Loopback)).Outcome);
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── PaymentsController ─────────────────────────────────────────────────────

    private static PaymentsController NewController(Mock<PaymentService> payments, string environment, int callerUserId = 7)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WebAppBaseUrl"] = "https://webapp.example" }).Build();
        var users = new Mock<User_db>("x");
        users.Setup(u => u.GetUserById(callerUserId)).ReturnsAsync(new User { UserId = callerUserId, Name = "Jan", Surname = "Jagter", Email = "jan@x.test" });
        var controller = new PaymentsController(payments.Object, Options.Create(new PayFastOptions()), env.Object, config, users.Object);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, callerUserId.ToString()) }, "Test")) };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.example");
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static Mock<PaymentService> PricedBooking()
    {
        var f = new Fakes();
        var payments = new Mock<PaymentService>(f.Bookings.Object, f.Profiles.Object, f.Ads.Object, f.Payments.Object, f.Settings);
        payments.Setup(p => p.QuoteAsync(PaymentKind.Booking, 5, 7, It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(QuoteResult.Ok(new PaymentQuote { Kind = PaymentKind.Booking, TargetId = 5, PayerUserId = 7, Amount = 3000m, ItemName = "Kudu" }));
        payments.Setup(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(PaymentApplyResult.Applied);
        return payments;
    }

    [Fact]
    public async Task Checkout_returns_a_form_with_the_server_price_signed()
    {
        var controller = NewController(PricedBooking(), Environments.Production);

        var result = await controller.Checkout(new PaymentItemRequest { Kind = "booking", Id = 5, ReturnUrl = "https://localhost:7285/payment?status=success", CancelUrl = "https://localhost:7285/jagter/bookings" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"amount\":3000", json);
        Assert.Contains("\"name\":\"signature\"", json);
        Assert.Contains("https://api.example/api/payfast/itn", json);
        Assert.Contains("\"simulatorAvailable\":false", json);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    public async Task Checkout_refuses_a_return_address_outside_the_WebApp(string? returnUrl)
    {
        var controller = NewController(PricedBooking(), Environments.Production);
        var result = await controller.Checkout(new PaymentItemRequest { Kind = "booking", Id = 5, ReturnUrl = returnUrl, CancelUrl = "https://localhost:7285/x" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Checkout_accepts_the_configured_WebAppBaseUrl_as_a_return_address()
    {
        var controller = NewController(PricedBooking(), Environments.Production);
        var result = await controller.Checkout(new PaymentItemRequest { Kind = "booking", Id = 5, ReturnUrl = "https://webapp.example/payment", CancelUrl = "https://webapp.example/x" });
        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task The_simulator_does_not_exist_outside_Development(string environment)
    {
        var payments = PricedBooking();
        var controller = NewController(payments, environment);

        Assert.IsType<NotFoundResult>(await controller.Simulate(new PaymentItemRequest { Kind = "booking", Id = 5 }));
        payments.Verify(p => p.ApplyAsync(It.IsAny<PaymentQuote>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task The_simulator_in_Development_credits_the_server_price()
    {
        var payments = PricedBooking();
        var controller = NewController(payments, Environments.Development);

        var result = await controller.Simulate(new PaymentItemRequest { Kind = "booking", Id = 5 });

        Assert.IsType<OkObjectResult>(result);
        payments.Verify(p => p.ApplyAsync(It.Is<PaymentQuote>(q => q.Amount == 3000m), It.Is<string>(r => r.StartsWith("PF-SIM-")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Paying_someone_elses_booking_is_refused_with_the_services_reason()
    {
        var f = new Fakes();
        f.Bookings.Setup(d => d.GetSpecificBooking(5)).ReturnsAsync(new BookingResponse { Id = 5, UserId = 99, Status = "Approved", TotalAmount = 10m });
        var controller = NewController(new Mock<PaymentService>(f.Bookings.Object, f.Profiles.Object, f.Ads.Object, f.Payments.Object, f.Settings) { CallBase = true }, Environments.Development);

        var result = await controller.Simulate(new PaymentItemRequest { Kind = "booking", Id = 5 });

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
        f.Bookings.Verify(d => d.SetPaymentStatus(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task A_subscription_is_always_for_the_caller_whatever_id_is_sent()
    {
        var f = new Fakes();
        var payments = new Mock<PaymentService>(f.Bookings.Object, f.Profiles.Object, f.Ads.Object, f.Payments.Object, f.Settings);
        payments.Setup(p => p.QuoteAsync(It.IsAny<PaymentKind>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(QuoteResult.Fail(403, "nee"));
        var controller = NewController(payments, Environments.Development);

        await controller.Simulate(new PaymentItemRequest { Kind = "subscription", Id = 12345, RoleCode = "PH", Months = 1 });

        payments.Verify(p => p.QuoteAsync(PaymentKind.Subscription, 7, 7, "PH", 1), Times.Once);
    }

    [Fact]
    public void Payment_endpoints_need_a_signed_in_caller_except_the_PayFast_notification()
    {
        Assert.NotEmpty(typeof(PaymentsController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
        Assert.Empty(typeof(PayFastController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
    }
}
