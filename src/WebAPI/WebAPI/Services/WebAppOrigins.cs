namespace WebAPI.Services;

/// <summary>
/// The browser origins the MikPunt WebApp is served from. Used for the CORS allowlist and
/// to decide where a payment may send the browser back to (no open redirects).
/// </summary>
public static class WebAppOrigins
{
    public static readonly string[] All =
    {
        "http://localhost:5180",
        "https://localhost:5180",
        "http://localhost:5085",
        "https://localhost:7251",
        "https://localhost:7180",
        "http://localhost:5147",
        "https://localhost:7285",
        "http://localhost:13386",
        "https://webapp.capstone.server",
        "http://webapp.capstone.server",
    };

    /// <summary>True for an absolute http(s) URL on one of <see cref="All"/> or on the configured WebAppBaseUrl.</summary>
    /// <summary>
    /// True when <paramref name="url"/> points at one of the WebApp's own origins: the built-in
    /// list, the configured WebAppBaseUrl, or any origin in <paramref name="extraOrigins"/>
    /// (config "WebAppAllowedOrigins", comma/semicolon separated - a deployment can be reached
    /// by more than one host name, e.g. an IP and a DNS name).
    /// </summary>
    public static bool IsAllowedUrl(string? url, string? configuredBaseUrl, string? extraOrigins = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (All.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        var configured = (extraOrigins ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(configuredBaseUrl ?? string.Empty);
        return configured.Any(c => Uri.TryCreate(c, UriKind.Absolute, out var allowed)
            && string.Equals(allowed.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase));
    }
}
