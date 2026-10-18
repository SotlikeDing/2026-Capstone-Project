using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebAPI.Database;
using WebAPI.Services;
using WebAPI.Services.Payments;

namespace WebAPI.Controllers;

/// <summary>What the WebApp asks to pay for. The amount is never part of it.</summary>
public class PaymentItemRequest
{
    /// <summary>booking, subscription or ad.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Booking id or ad id (ignored for a subscription - that's always the caller's own).</summary>
    public int Id { get; set; }
    public string? RoleCode { get; set; }
    public int Months { get; set; }
    /// <summary>Where PayFast sends the browser afterwards; must be on a MikPunt WebApp origin.</summary>
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
}

/// <summary>
/// Starts payments. <c>checkout</c> returns a PayFast form the server priced and signed;
/// the payment is only credited when PayFast's notification arrives (PayFastController).
/// <c>simulate</c> credits a payment without PayFast and exists only in Development,
/// because PayFast can't send its notification to a developer's localhost.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;
    private readonly PayFastOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly User_db _users;

    public PaymentsController(PaymentService payments, IOptions<PayFastOptions> options, IWebHostEnvironment env, IConfiguration config, User_db users)
    {
        _payments = payments;
        _options = options.Value;
        _env = env;
        _config = config;
        _users = users;
    }

    /// <summary>The admin's Enable_PayFast_Sandbox flag, or null without a flag service (tests).</summary>
    private async Task<bool?> PayFastSandboxAsync()
    {
        var flags = HttpContext?.RequestServices?.GetService<WebAPI.Services.IFeatureFlags>();
        return flags == null ? null : await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.PayFastSandbox, true);
    }

    private bool SimulatorAvailable => _env.IsDevelopment() && _options.EnableSimulator;

    private int CallerUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private async Task<(QuoteResult? Result, IActionResult? Error)> QuoteAsync(PaymentItemRequest? request)
    {
        if (request == null || !TryParseKind(request.Kind, out var kind))
            return (null, BadRequest(new { message = "Onbekende betaling." }));
        int caller = CallerUserId;
        int target = kind == PaymentKind.Subscription ? caller : request.Id;
        var quote = await _payments.QuoteAsync(kind, target, caller, request.RoleCode, request.Months);
        return (quote, null);
    }

    private static bool TryParseKind(string? kind, out PaymentKind result)
    {
        result = PaymentKind.Booking;
        switch (kind?.Trim().ToLowerInvariant())
        {
            case "booking": result = PaymentKind.Booking; return true;
            case "subscription": result = PaymentKind.Subscription; return true;
            case "ad": result = PaymentKind.Ad; return true;
            default: return false;
        }
    }

    /// <summary>The PayFast form for one item: amount, description and signature all made here.</summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] PaymentItemRequest request)
    {
        var (quote, error) = await QuoteAsync(request);
        if (error != null) return error;
        if (quote!.Quote == null)
            return StatusCode(quote.StatusCode, new { message = quote.Error, alreadyPaid = quote.AlreadyPaid });

        string webAppBase = _config["WebAppBaseUrl"] ?? string.Empty;
        string? extraOrigins = _config["WebAppAllowedOrigins"];
        if (!WebAppOrigins.IsAllowedUrl(request.ReturnUrl, webAppBase, extraOrigins) || !WebAppOrigins.IsAllowedUrl(request.CancelUrl, webAppBase, extraOrigins))
            return BadRequest(new { message = "Ongeldige terugkeer-adres." });

        string notifyUrl = !string.IsNullOrWhiteSpace(_options.NotifyUrl)
            ? _options.NotifyUrl
            : $"{Request.Scheme}://{Request.Host}/api/payfast/itn";

        var payer = await _users.GetUserById(CallerUserId);
        var fields = PayFastCheckout.BuildFields(_options, quote.Quote, request.ReturnUrl!, request.CancelUrl!, notifyUrl,
                                                 payer?.Name, payer?.Surname, payer?.Email ?? User.FindFirstValue(ClaimTypes.Email));
        return Ok(new
        {
            processUrl = _options.ProcessUrlFor(await PayFastSandboxAsync()),
            fields = fields.Select(f => new { name = f.Key, value = f.Value }),
            amount = quote.Quote.Amount,
            itemName = quote.Quote.ItemName,
            months = quote.Quote.Months,
            simulatorAvailable = SimulatorAvailable,
        });
    }

    /// <summary>The price the server would charge, without starting a payment (for showing totals).</summary>
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] PaymentItemRequest request)
    {
        var (quote, error) = await QuoteAsync(request);
        if (error != null) return error;
        if (quote!.Quote == null)
            return StatusCode(quote.StatusCode, new { message = quote.Error, alreadyPaid = quote.AlreadyPaid });
        return Ok(new { amount = quote.Quote.Amount, itemName = quote.Quote.ItemName, months = quote.Quote.Months, simulatorAvailable = SimulatorAvailable });
    }

    /// <summary>
    /// Development only: credits the item as if PayFast had confirmed it, with the same
    /// server-side price and checks. Answers 404 in every other environment.
    /// </summary>
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] PaymentItemRequest request)
    {
        if (!SimulatorAvailable) return NotFound();

        var (quote, error) = await QuoteAsync(request);
        if (error != null) return error;
        if (quote!.AlreadyPaid) return Ok(new { success = true, alreadyPaid = true });
        if (quote.Quote == null)
            return StatusCode(quote.StatusCode, new { message = quote.Error });

        string reference = "PF-SIM-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var applied = await _payments.ApplyAsync(quote.Quote, reference, "Simulator (Development)");
        return applied switch
        {
            PaymentApplyResult.NotFound => NotFound(new { message = "Item nie gevind nie." }),
            PaymentApplyResult.AlreadyPaid => Ok(new { success = true, alreadyPaid = true }),
            _ => Ok(new { success = true, alreadyPaid = false, reference, amount = quote.Quote.Amount }),
        };
    }
}
