using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;

namespace WebAPI.Services.Payments;

public enum ItnOutcome { Applied, AlreadyPaid, Ignored, Rejected, RetryLater }

/// <summary>What happened to one PayFast notification, and why (for the log and the tests).</summary>
public sealed record ItnResult(ItnOutcome Outcome, string Reason);

/// <summary>
/// Checks a PayFast Instant Transaction Notification the way PayFast prescribes before
/// anything is marked paid: the signature (with the merchant passphrase), our merchant id,
/// that it came from a PayFast host, that PayFast itself confirms it, and that the amount
/// is exactly what the server would charge for that item. Only then is it credited.
/// </summary>
public class PayFastItnService
{
    public const string HttpClientName = "payfast";

    private readonly PayFastOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentService _payments;
    private readonly ILogger<PayFastItnService> _logger;
    private readonly WebAPI.Services.IFeatureFlags? _flags;

    public PayFastItnService(IOptions<PayFastOptions> options, IHttpClientFactory httpFactory, PaymentService payments, ILogger<PayFastItnService> logger,
        WebAPI.Services.IFeatureFlags? flags = null)
    {
        _flags = flags;
        _options = options.Value;
        _httpFactory = httpFactory;
        _payments = payments;
        _logger = logger;
    }

    public async Task<ItnResult> ProcessAsync(string body, IPAddress? remoteIp)
    {
        var fields = PayFastCheckout.ParseBody(body);
        string Get(string key) => fields.LastOrDefault(f => f.Key == key).Value ?? string.Empty;

        var expected = PayFastSignature.Sign(fields, _options.Passphrase, skipEmpty: false);
        if (!PayFastSignature.Matches(expected, Get("signature")))
            return Reject("signature does not match");
        if (!string.Equals(Get("merchant_id"), _options.MerchantId, StringComparison.Ordinal))
            return Reject("notification is for another merchant");
        if (_options.VerifySourceIp && !await IsFromPayFastAsync(remoteIp))
            return Reject($"sender {remoteIp} is not a PayFast host");

        if (!string.Equals(Get("payment_status"), "COMPLETE", StringComparison.OrdinalIgnoreCase))
            return new ItnResult(ItnOutcome.Ignored, $"payment_status is {Get("payment_status")}");

        if (!PayFastCheckout.TryParseKind(Get(PayFastCheckout.FieldKind), out var kind)
            || !int.TryParse(Get(PayFastCheckout.FieldTarget), NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId)
            || !int.TryParse(Get(PayFastCheckout.FieldPayer), NumberStyles.Integer, CultureInfo.InvariantCulture, out int payerId))
            return Reject("unknown item in custom fields");
        int.TryParse(Get(PayFastCheckout.FieldMonths), NumberStyles.Integer, CultureInfo.InvariantCulture, out int months);

        string reference = Get("pf_payment_id");
        if (string.IsNullOrWhiteSpace(reference))
            return Reject("no pf_payment_id");

        var quote = await _payments.QuoteAsync(kind, targetId, payerId, Get(PayFastCheckout.FieldRole), months);
        if (quote.AlreadyPaid)
            return new ItnResult(ItnOutcome.AlreadyPaid, $"{kind} {targetId} was already paid");
        if (quote.Quote == null)
        {
            _logger.LogError("PayFast payment {Reference} for {Kind} {Target} can't be credited ({Error}) - reconcile it by hand.", reference, kind, targetId, quote.Error);
            return Reject($"item can't be paid: {quote.Error}");
        }

        if (!decimal.TryParse(Get("amount_gross"), NumberStyles.Number, CultureInfo.InvariantCulture, out var paid)
            || Math.Abs(paid - quote.Quote.Amount) > 0.01m)
            return Reject($"amount {Get("amount_gross")} differs from the expected {quote.Quote.Amount:F2}");

        if (_options.ValidateWithServer)
        {
            var confirmed = await ConfirmWithPayFastAsync(PayFastSignature.ParamString(fields, skipEmpty: false));
            if (confirmed == null)
                return new ItnResult(ItnOutcome.RetryLater, "could not reach PayFast to confirm; PayFast will resend");
            if (confirmed == false)
                return Reject("PayFast did not confirm the notification");
        }

        var applied = await _payments.ApplyAsync(quote.Quote, reference.Trim(), "PayFast");
        return applied switch
        {
            PaymentApplyResult.Applied => new ItnResult(ItnOutcome.Applied, $"{kind} {targetId} paid ({reference})"),
            PaymentApplyResult.AlreadyPaid => new ItnResult(ItnOutcome.AlreadyPaid, $"{kind} {targetId} was already paid ({reference})"),
            _ => Reject($"{kind} {targetId} not found"),
        };
    }

    private ItnResult Reject(string reason)
    {
        _logger.LogWarning("PayFast notification rejected: {Reason}", reason);
        return new ItnResult(ItnOutcome.Rejected, reason);
    }

    /// <summary>Whether the sender's address belongs to one of PayFast's hosts (resolved now, since PayFast's addresses change).</summary>
    protected virtual async Task<bool> IsFromPayFastAsync(IPAddress? remoteIp)
    {
        if (remoteIp == null) return false;
        if (remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4();
        foreach (var host in _options.ValidHosts)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                if (addresses.Any(a => a.Equals(remoteIp))) return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve PayFast host {Host}", host);
            }
        }
        return false;
    }

    /// <summary>Posts the notification back to PayFast's validate endpoint, which answers VALID for a genuine one. Null when PayFast couldn't be reached.</summary>
    protected virtual async Task<bool?> ConfirmWithPayFastAsync(string paramString)
    {
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var content = new StringContent(paramString, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            bool? sandbox = _flags == null ? null : await _flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.PayFastSandbox, true);
            using var response = await client.PostAsync(_options.ValidateUrlFor(sandbox), content);
            if (!response.IsSuccessStatusCode) return null;
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            return string.Equals(text, "VALID", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayFast validate call failed");
            return null;
        }
    }
}
