using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Services.Payments;

/// <summary>What is being paid for.</summary>
public enum PaymentKind { Booking, Subscription, Ad }

/// <summary>
/// A price the server worked out for one payment - never taken from the client. The
/// checkout form, the PayFast notification check and the local simulator all use it.
/// </summary>
public sealed class PaymentQuote
{
    public PaymentKind Kind { get; init; }
    /// <summary>Booking id, ad id, or (for a subscription) the payer's own user id.</summary>
    public int TargetId { get; init; }
    public int PayerUserId { get; init; }
    /// <summary>Subscription role code (PH, VG, Boer, PJ); empty otherwise.</summary>
    public string RoleCode { get; init; } = string.Empty;
    /// <summary>Months bought (subscription/ad); 0 for a booking.</summary>
    public int Months { get; init; }
    public decimal Amount { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ItemDescription { get; init; } = string.Empty;
}

/// <summary>Result of <see cref="PaymentService.QuoteAsync"/>: a quote, or why there isn't one (with the HTTP status to answer).</summary>
public sealed class QuoteResult
{
    public PaymentQuote? Quote { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? Error { get; init; }
    /// <summary>The booking is already paid - nothing to charge.</summary>
    public bool AlreadyPaid { get; init; }

    public static QuoteResult Ok(PaymentQuote quote) => new() { Quote = quote };
    public static QuoteResult Fail(int statusCode, string error, bool alreadyPaid = false) => new() { StatusCode = statusCode, Error = error, AlreadyPaid = alreadyPaid };
}

public enum PaymentApplyResult { Applied, AlreadyPaid, NotFound }

/// <summary>
/// The only place a payment is priced and credited. Prices come from the database
/// (the booking's confirmed total) and the admin pricing settings, so nothing in a
/// URL or request body can change what is charged or what gets marked paid.
/// </summary>
public class PaymentService
{
    /// <summary>Subscription role codes that can be paid for (as used by the WebApp) and the roles.roleCode each needs.</summary>
    public static readonly IReadOnlyDictionary<string, string> SubscriptionRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PH"] = "PH",
        ["VG"] = "VG",
        ["Boer"] = "FARMER",
        ["PJ"] = "PJ",
    };

    public static readonly int[] SubscriptionMonths = { 1, 3, 6, 12 };
    public const int MaxAdMonths = 36;

    private readonly Bookings_db _bookings;
    private readonly Profile_db _profiles;
    private readonly Ads_db _ads;
    private readonly Payments_db _payments;
    private readonly ISystemSettingsCache _settings;

    public PaymentService(Bookings_db bookings, Profile_db profiles, Ads_db ads, Payments_db payments, ISystemSettingsCache settings)
    {
        _bookings = bookings;
        _profiles = profiles;
        _ads = ads;
        _payments = payments;
        _settings = settings;
    }

    /// <summary>Rand per US dollar (admin setting UsdToZarRate), used to charge a US$ quote in rand.</summary>
    public decimal UsdToZarRate => Math.Max(0.01m, _settings.GetDecimal("UsdToZarRate", 18m));

    /// <summary>Admin setting PackagePricesInZar: package prices (and quotes without their own currency) are in rand.</summary>
    public bool PackagePricesInZar => _settings.GetDecimal("PackagePricesInZar", 0m) > 0m;

    /// <summary>
    /// The currency the outfitter quoted the booking in (its price breakdown's Currency). A booking
    /// without one was priced from the package rates, so it is in the package currency.
    /// </summary>
    public static string BookingCurrency(string? priceBreakdownJson, string packageCurrency = "USD")
    {
        if (string.IsNullOrWhiteSpace(priceBreakdownJson)) return packageCurrency;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(priceBreakdownJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Name.Equals("Currency", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        return string.Equals(prop.Value.GetString(), "ZAR", StringComparison.OrdinalIgnoreCase) ? "ZAR" : "USD";
            return packageCurrency;
        }
        catch (System.Text.Json.JsonException)
        {
            return packageCurrency;
        }
    }

    /// <summary>Deposit share of a booking's confirmed total (admin setting BookingDepositPercentage, 1-100).</summary>
    public decimal DepositPercentage => Math.Clamp(_settings.GetDecimal("BookingDepositPercentage", 100m), 1m, 100m);

    /// <summary>Subscription price for a role and term: 5% off 3 months, 10% off 6, 12 months for the price of 10.</summary>
    public decimal SubscriptionPrice(string roleCode, int months)
    {
        decimal monthly = _settings.GetDecimal("SubscriptionPrice_" + CanonicalRole(roleCode), 299m);
        return months switch
        {
            1 => monthly,
            3 => Math.Round(monthly * 3m * 0.95m, 2),
            6 => Math.Round(monthly * 6m * 0.90m, 2),
            12 => monthly * 10m,
            _ => monthly * months,
        };
    }

    /// <summary>Ad price: months x AdMonthlyPrice, 5% off from 3 months, 10% from 6, 20% from 12.</summary>
    public decimal AdPrice(int months)
    {
        decimal discount = months switch { >= 12 => 0.20m, >= 6 => 0.10m, >= 3 => 0.05m, _ => 0m };
        decimal gross = months * _settings.GetDecimal("AdMonthlyPrice", 100m);
        return Math.Round(gross - gross * discount, 2);
    }

    /// <summary>The role code spelled the way the settings keys and subscriptions table use it (PH, VG, Boer, PJ).</summary>
    private static string CanonicalRole(string roleCode)
        => SubscriptionRoles.Keys.FirstOrDefault(k => string.Equals(k, roleCode, StringComparison.OrdinalIgnoreCase)) ?? roleCode;

    /// <summary>
    /// Works out what <paramref name="payerUserId"/> owes for this item, or why they can't pay
    /// for it: only the booking's own hunter pays its deposit, and only once the outfitter has
    /// approved it; only a holder of the role pays for that role's listing; only the ad's
    /// advertiser pays for the ad.
    /// </summary>
    public virtual async Task<QuoteResult> QuoteAsync(PaymentKind kind, int targetId, int payerUserId, string? roleCode, int months)
    {
        if (payerUserId <= 0) return QuoteResult.Fail(401, "Teken asseblief in om te betaal.");

        switch (kind)
        {
            case PaymentKind.Booking:
            {
                var booking = await _bookings.GetSpecificBooking(targetId);
                if (booking == null || booking.UserId != payerUserId)
                    return QuoteResult.Fail(404, "Hierdie bespreking is nie gevind onder u besprekings nie.");
                if (IsPaid(booking.Status) || string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
                    return QuoteResult.Fail(409, "Hierdie bespreking is reeds betaal.", alreadyPaid: true);
                if (!string.Equals(booking.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                    return QuoteResult.Fail(409, "Hierdie bespreking is nog nie deur die uitruster goedgekeur nie. U kan betaal sodra dit goedgekeur is.");
                if (booking.TotalAmount <= 0)
                    return QuoteResult.Fail(409, "Hierdie bespreking het nog geen bevestigde prys nie. Kontak asseblief ondersteuning.");

                var title = !string.IsNullOrWhiteSpace(booking.PakketName) ? booking.PakketName : (string.IsNullOrWhiteSpace(booking.FarmName) ? "Jag Bespreking" : booking.FarmName);
                // PayFast only takes rand: a US$ quote is charged at the admin's exchange rate.
                bool usd = BookingCurrency(booking.PriceBreakdownJson, PackagePricesInZar ? "ZAR" : "USD") == "USD";
                decimal rate = UsdToZarRate;
                decimal totalZar = usd ? booking.TotalAmount * rate : booking.TotalAmount;
                return QuoteResult.Ok(new PaymentQuote
                {
                    Kind = kind,
                    TargetId = booking.Id,
                    PayerUserId = payerUserId,
                    Amount = Math.Round(totalZar * DepositPercentage / 100m, 2),
                    ItemName = title,
                    ItemDescription = usd
                        ? $"MikPunt Ekspedisie Deposito Bespreking #{booking.Id} (US$ {booking.TotalAmount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} teen R{rate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}/US$)"
                        : $"MikPunt Ekspedisie Deposito Bespreking #{booking.Id}",
                });
            }

            case PaymentKind.Subscription:
            {
                if (string.IsNullOrWhiteSpace(roleCode) || !SubscriptionRoles.TryGetValue(roleCode, out var dbRole))
                    return QuoteResult.Fail(400, "Onbekende rol vir 'n intekening.");
                if (!SubscriptionMonths.Contains(months))
                    return QuoteResult.Fail(400, "Kies 1, 3, 6 of 12 maande.");
                if (!await _payments.UserHasRole(payerUserId, dbRole))
                    return QuoteResult.Fail(403, "U het nie hierdie rol op u profiel nie.");

                var role = CanonicalRole(roleCode);
                return QuoteResult.Ok(new PaymentQuote
                {
                    Kind = kind,
                    TargetId = payerUserId,
                    PayerUserId = payerUserId,
                    RoleCode = role,
                    Months = months,
                    Amount = SubscriptionPrice(role, months),
                    ItemName = $"MikPunt {role} Profiel Advertensie ({months} Maande)",
                    ItemDescription = $"MikPunt {months}-maande profiel advertensie vir {role}",
                });
            }

            case PaymentKind.Ad:
            {
                if (months < 1 || months > MaxAdMonths)
                    return QuoteResult.Fail(400, $"Kies tussen 1 en {MaxAdMonths} maande.");
                var ad = await _ads.GetAd(targetId);
                if (ad == null || ad.UserId != payerUserId)
                    return QuoteResult.Fail(404, "Hierdie advertensie is nie gevind onder u advertensies nie.");

                return QuoteResult.Ok(new PaymentQuote
                {
                    Kind = kind,
                    TargetId = ad.Id,
                    PayerUserId = payerUserId,
                    Months = months,
                    Amount = AdPrice(months),
                    ItemName = $"MikPunt Advertensie: {ad.Title}",
                    ItemDescription = $"Advertensie #{ad.Id} vir {months} maand(e)",
                });
            }

            default:
                return QuoteResult.Fail(400, "Onbekende betaling.");
        }
    }

    private static bool IsPaid(string? status)
        => string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Credits a payment that was really made (a verified PayFast notification, or the
    /// Development simulator). Idempotent on <paramref name="paymentReference"/> for every kind,
    /// because PayFast retries its notification.
    /// </summary>
    public virtual async Task<PaymentApplyResult> ApplyAsync(PaymentQuote quote, string paymentReference, string paymentMethod)
    {
        switch (quote.Kind)
        {
            case PaymentKind.Booking:
                return await _bookings.SetPaymentStatus(quote.TargetId, "Paid", paymentReference) switch
                {
                    Bookings_db.PaymentStatusResult.NotFound => PaymentApplyResult.NotFound,
                    Bookings_db.PaymentStatusResult.AlreadyPaid => PaymentApplyResult.AlreadyPaid,
                    _ => PaymentApplyResult.Applied,
                };

            case PaymentKind.Subscription:
            {
                var before = await _profiles.GetRoleSubscription(quote.PayerUserId, quote.RoleCode);
                if (before != null && string.Equals(before.LastPaymentReference, paymentReference, StringComparison.Ordinal)
                    && string.Equals(before.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
                    return PaymentApplyResult.AlreadyPaid;
                await _profiles.ProcessSubscriptionPayment(quote.PayerUserId, quote.RoleCode, quote.Months, quote.Amount, paymentReference);
                return PaymentApplyResult.Applied;
            }

            case PaymentKind.Ad:
            {
                var ad = await _ads.GetAd(quote.TargetId);
                if (ad == null) return PaymentApplyResult.NotFound;
                if (string.Equals(ad.PaymentRef, paymentReference, StringComparison.Ordinal)) return PaymentApplyResult.AlreadyPaid;
                // Extend a running ad from its current end; restart a lapsed or unpaid one from now.
                var now = DateTime.Now;
                var start = ad.ExpiryDate.HasValue && ad.ExpiryDate.Value > now && !string.IsNullOrWhiteSpace(ad.PaymentRef) ? ad.ExpiryDate.Value : now;
                var ok = await _ads.RecordPayment(ad.Id, new RecordPaymentRequest
                {
                    PaymentRef = paymentReference,
                    Months = quote.Months,
                    Amount = quote.Amount,
                    PeriodStartDate = start,
                    PeriodEndDate = start.AddMonths(quote.Months),
                    PaymentMethod = paymentMethod,
                });
                return ok ? PaymentApplyResult.Applied : PaymentApplyResult.NotFound;
            }

            default:
                return PaymentApplyResult.NotFound;
        }
    }
}
