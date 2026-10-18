using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WebAPI.Services.Payments;

/// <summary>
/// "PayFast" configuration section. The defaults are PayFast's own published sandbox
/// merchant (not a secret); production sets its own merchant, passphrase, live URLs and a
/// public NotifyUrl that PayFast can reach.
/// </summary>
public sealed class PayFastOptions
{
    public string MerchantId { get; set; } = "10000100";
    public string MerchantKey { get; set; } = "46f0cd694581a";
    /// <summary>The merchant account's passphrase; signs the checkout and the notification. Empty = account has none.</summary>
    public string Passphrase { get; set; } = string.Empty;
    public string ProcessUrl { get; set; } = "https://sandbox.payfast.co.za/eng/process";
    public string ValidateUrl { get; set; } = "https://sandbox.payfast.co.za/eng/query/validate";
    /// <summary>Public URL of api/payfast/itn. Empty = built from the incoming request (fine locally, but PayFast can't reach localhost).</summary>
    public string NotifyUrl { get; set; } = string.Empty;
    /// <summary>Ask PayFast to confirm every notification (recommended by PayFast).</summary>
    public bool ValidateWithServer { get; set; } = true;
    /// <summary>Only accept notifications from PayFast's own hosts. Switch off only behind a proxy that hides the caller's address.</summary>
    public bool VerifySourceIp { get; set; } = true;
    public string[] ValidHosts { get; set; } = { "www.payfast.co.za", "sandbox.payfast.co.za", "w1w.payfast.co.za", "w2w.payfast.co.za" };
    /// <summary>Allow the local "simulate payment" endpoint. Only ever honoured in the Development environment.</summary>
    public bool EnableSimulator { get; set; } = true;

    public const string SandboxProcessUrl = "https://sandbox.payfast.co.za/eng/process";
    public const string SandboxValidateUrl = "https://sandbox.payfast.co.za/eng/query/validate";
    public const string LiveProcessUrl = "https://www.payfast.co.za/eng/process";
    public const string LiveValidateUrl = "https://www.payfast.co.za/eng/query/validate";

    /// <summary>
    /// Checkout URL for the admin's Enable_PayFast_Sandbox flag (null = no flag service: use the
    /// configured URL). Live payments also need the live merchant id/key/passphrase in the config.
    /// </summary>
    public string ProcessUrlFor(bool? sandbox) => sandbox switch { true => SandboxProcessUrl, false => LiveProcessUrl, null => ProcessUrl };

    /// <summary>Where PayFast confirms a notification, for the same sandbox/live mode.</summary>
    public string ValidateUrlFor(bool? sandbox) => sandbox switch { true => SandboxValidateUrl, false => LiveValidateUrl, null => ValidateUrl };
}

/// <summary>PayFast's signature: MD5 over the fields as PHP's urlencode would write them, plus the passphrase.</summary>
public static class PayFastSignature
{
    /// <summary>PHP urlencode(): letters, digits and -_. stay; space is +; everything else is %XX (upper case) per UTF-8 byte.</summary>
    public static string UrlEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('+');
            else
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// key=value&amp;... in the given order, without the signature itself. A checkout leaves out
    /// empty fields; a notification is signed over every field PayFast posted, empty ones included.
    /// </summary>
    public static string ParamString(IEnumerable<KeyValuePair<string, string>> fields, bool skipEmpty = true)
        => string.Join("&", fields
            .Where(f => f.Key != "signature" && (!skipEmpty || !string.IsNullOrEmpty(f.Value?.Trim())))
            .Select(f => $"{f.Key}={UrlEncode((f.Value ?? string.Empty).Trim())}"));

    public static string Sign(IEnumerable<KeyValuePair<string, string>> fields, string? passphrase, bool skipEmpty = true)
    {
        var data = ParamString(fields, skipEmpty);
        if (!string.IsNullOrWhiteSpace(passphrase))
            data += "&passphrase=" + UrlEncode(passphrase.Trim());
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison so a forged signature can't be found byte by byte.</summary>
    public static bool Matches(string expected, string? actual)
        => actual != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual.Trim().ToLowerInvariant()));
}

/// <summary>Builds the PayFast checkout form and reads back the custom fields from a notification.</summary>
public static class PayFastCheckout
{
    public const string FieldKind = "custom_str1";
    public const string FieldRole = "custom_str2";
    public const string FieldTarget = "custom_int1";
    public const string FieldMonths = "custom_int2";
    public const string FieldPayer = "custom_int3";

    public static string KindCode(PaymentKind kind) => kind switch
    {
        PaymentKind.Booking => "BOOKING",
        PaymentKind.Subscription => "SUBSCRIPTION",
        _ => "AD",
    };

    public static bool TryParseKind(string? code, out PaymentKind kind)
    {
        kind = PaymentKind.Booking;
        switch (code?.Trim().ToUpperInvariant())
        {
            case "BOOKING": kind = PaymentKind.Booking; return true;
            case "SUBSCRIPTION": kind = PaymentKind.Subscription; return true;
            case "AD": kind = PaymentKind.Ad; return true;
            default: return false;
        }
    }

    /// <summary>Signed form fields, in the order PayFast's documentation lists them (the signature depends on it).</summary>
    public static List<KeyValuePair<string, string>> BuildFields(PayFastOptions options, PaymentQuote quote, string returnUrl, string cancelUrl, string notifyUrl,
                                                                 string? firstName, string? lastName, string? email)
    {
        static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
        var fields = new List<KeyValuePair<string, string>>
        {
            new("merchant_id", options.MerchantId),
            new("merchant_key", options.MerchantKey),
            new("return_url", returnUrl),
            new("cancel_url", cancelUrl),
            new("notify_url", notifyUrl),
            new("name_first", Cut(firstName?.Trim() ?? "", 100)),
            new("name_last", Cut(lastName?.Trim() ?? "", 100)),
            new("email_address", Cut(email?.Trim() ?? "", 100)),
            new("m_payment_id", $"{KindCode(quote.Kind)}-{quote.TargetId}-{Guid.NewGuid():N}"),
            new("amount", quote.Amount.ToString("F2", CultureInfo.InvariantCulture)),
            new("item_name", Cut(quote.ItemName, 100)),
            new("item_description", Cut(quote.ItemDescription, 255)),
            new(FieldTarget, quote.TargetId.ToString(CultureInfo.InvariantCulture)),
            new(FieldMonths, quote.Months.ToString(CultureInfo.InvariantCulture)),
            new(FieldPayer, quote.PayerUserId.ToString(CultureInfo.InvariantCulture)),
            new(FieldKind, KindCode(quote.Kind)),
            new(FieldRole, quote.RoleCode),
        };
        fields.RemoveAll(f => string.IsNullOrEmpty(f.Value));
        fields.Add(new("signature", PayFastSignature.Sign(fields, options.Passphrase)));
        return fields;
    }

    /// <summary>Splits an application/x-www-form-urlencoded body, keeping PayFast's field order (the signature depends on it).</summary>
    public static List<KeyValuePair<string, string>> ParseBody(string body)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = WebUtility.UrlDecode(eq < 0 ? part : part[..eq]);
            string value = eq < 0 ? string.Empty : WebUtility.UrlDecode(part[(eq + 1)..]);
            list.Add(new(key, value));
        }
        return list;
    }
}
