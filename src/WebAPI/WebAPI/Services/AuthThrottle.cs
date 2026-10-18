using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace WebAPI.Services;

/// <summary>
/// Throttles the anonymous account endpoints (sign-in, register, password reset, e-mail
/// verification) that the endpoint rate-limit policies don't cover:
/// - per client IP, against password spraying and sign-up floods;
/// - per e-mail address on the endpoints that send mail, so nobody can e-mail-bomb a user;
/// - optionally, a lockout after repeated failed sign-ins for one address (paths in
///   <see cref="AuthThrottleRules.FailureLockoutPaths"/>; 401 counts as a failure).
/// Counters live in memory per instance, which is enough for one container per API.
/// </summary>
public sealed class AuthThrottleRules
{
    public Dictionary<string, (int Limit, TimeSpan Window)> PerIp { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, (int Limit, TimeSpan Window)> PerEmail { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailureLockoutPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaxFailures { get; set; } = 5;
    public TimeSpan FailureWindow { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class AuthThrottleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly AuthThrottleRules _rules;

    public AuthThrottleMiddleware(RequestDelegate next, IMemoryCache cache, AuthThrottleRules rules)
    {
        _next = next;
        _cache = cache;
        _rules = rules;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = (context.Request.Path.Value ?? "").TrimEnd('/');
        bool ipRule = _rules.PerIp.TryGetValue(path, out var ip);
        bool emailRule = _rules.PerEmail.TryGetValue(path, out var mail);
        bool lockout = _rules.FailureLockoutPaths.Contains(path);
        if (!ipRule && !emailRule && !lockout)
        {
            await _next(context);
            return;
        }

        string client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (ipRule && !Allow($"ip|{path}|{client}", ip.Limit, ip.Window))
        {
            await Reject(context, ip.Window);
            return;
        }

        string? email = (emailRule || lockout) ? await ReadEmailAsync(context.Request) : null;
        if (emailRule && email != null && !Allow($"mail|{path}|{email}", mail.Limit, mail.Window))
        {
            await Reject(context, mail.Window);
            return;
        }

        string failKey = $"fail|{path}|{email}";
        if (lockout && email != null && _cache.TryGetValue(failKey, out Counter? fails) && fails!.Value >= _rules.MaxFailures)
        {
            await Reject(context, _rules.FailureWindow);
            return;
        }

        await _next(context);

        if (lockout && email != null)
        {
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                Increment(failKey, _rules.FailureWindow);
            else if (context.Response.StatusCode == StatusCodes.Status200OK)
                _cache.Remove(failKey);
        }
    }

    private bool Allow(string key, int limit, TimeSpan window) => Increment(key, window) <= limit;

    private int Increment(string key, TimeSpan window)
    {
        var counter = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return new Counter();
        })!;
        return Interlocked.Increment(ref counter.Value);
    }

    private static async Task Reject(HttpContext context, TimeSpan retryAfter)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        int minutes = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes));
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = $"Te veel pogings. Probeer asseblief weer oor {minutes} {(minutes == 1 ? "minuut" : "minute")}."
        });
    }

    /// <summary>The e-mail from the query string or a small JSON body (lower-cased), leaving the body readable for the controller.</summary>
    private static async Task<string?> ReadEmailAsync(HttpRequest request)
    {
        string? email = request.Query["email"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(email) && request.ContentLength is > 0 and < 16_384
            && request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            request.EnableBuffering();
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.NameEquals("email") || prop.Name.Equals("email", StringComparison.OrdinalIgnoreCase))
                            email = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
            }
            catch (JsonException)
            {
                // Not our problem here - the controller's model binding reports it.
            }
            finally
            {
                request.Body.Position = 0;
            }
        }
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private sealed class Counter
    {
        public int Value;
    }
}
