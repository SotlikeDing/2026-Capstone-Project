using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services.Ai;

namespace WebAPI.Controllers;

/// <summary>
/// The AI Helper: price-and-package optimisation for PJ, PH, VG, FARMER (Boer) and AP users. Every endpoint requires a valid
/// bearer token; a user can only ask about a role they actually hold (Admin may use any). The admin endpoints expose the
/// learning loop - export real observations/feedback for retraining, harvest now, and hot-reload a retrained model.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiHelperController : ControllerBase
{
    private readonly IPackageAdvisor _advisor;
    private readonly IAiHelperStore _store;
    private readonly IPriceModel _model;
    private readonly ILlmClient _llm;
    private readonly AiHelperOptions _options;
    private readonly ITeamMatcher? _team;

    public AiHelperController(IPackageAdvisor advisor, IAiHelperStore store, IPriceModel model, ILlmClient llm, IOptions<AiHelperOptions> options,
        ITeamMatcher? team = null)
    {
        _advisor = advisor;
        _store = store;
        _model = model;
        _llm = llm;
        _options = options.Value;
        _team = team;
    }

    private int CallerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private bool IsAdmin => User.FindAll(ClaimTypes.Role).Any(r => string.Equals(r.Value, "Admin", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Scores the draft offer with the trained price model, pulls comparables, and asks the LLM for Afrikaans advice grounded in
    /// those numbers. Always returns the numbers; the advice text is best-effort (see NarrativeStatus).
    /// </summary>
    [HttpPost("optimize")]
    [EnableRateLimiting("AiPolicy")]
    public async Task<IActionResult> Optimize([FromBody] AiOptimizeRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { message = "Versoek ontbreek." });
        var role = PackageAdvisorService.NormalizeRole(request.RoleCode);
        if (role == null) return BadRequest(new { message = "Ongeldige rol vir die AI-helper." });

        int userId = CallerId;
        if (userId <= 0) return Unauthorized();

        bool holdsRole = IsAdmin || User.FindAll(ClaimTypes.Role).Any(r => PackageAdvisorService.NormalizeRole(r.Value) == role);
        if (!holdsRole) return StatusCode(403, new { message = "U het nie hierdie rol nie." });

        try
        {
            return Ok(await _advisor.OptimizeAsync(request, userId, ct));
        }
        catch (AiValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AiModelUnavailableException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Smart Teaming: ranked partner suggestions for the caller's role (PJ -> PH/VG/Boer, PH/VG/Boer -> PJ), each with the score
    /// components, the facts behind them and an LLM-written reason. AP gets an exclusion notice instead of matches.
    /// </summary>
    [HttpPost("team-matches")]
    [EnableRateLimiting("AiPolicy")]
    public async Task<IActionResult> TeamMatches([FromBody] AiTeamRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { message = "Versoek ontbreek." });
        var role = PackageAdvisorService.NormalizeRole(request.RoleCode);
        if (role == null) return BadRequest(new { message = "Ongeldige rol vir die AI-helper." });

        int userId = CallerId;
        if (userId <= 0) return Unauthorized();

        bool holdsRole = IsAdmin || User.FindAll(ClaimTypes.Role).Any(r => PackageAdvisorService.NormalizeRole(r.Value) == role);
        if (!holdsRole) return StatusCode(403, new { message = "U het nie hierdie rol nie." });
        if (_team == null) return StatusCode(503, new { message = "Slim spanbou is nie beskikbaar nie." });

        try
        {
            return Ok(await _team.FindAsync(request, userId, ct));
        }
        catch (AiValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Thumbs-up/down on a piece of advice. Only the user the advice was produced for can rate it; helpful advice is exportable (admin/advice-log) to judge and tune the prompt.</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] AiFeedbackRequest request)
    {
        if (request == null || request.AdviceId <= 0) return BadRequest(new { message = "Ongeldige advies-verwysing." });
        int userId = CallerId;
        if (userId <= 0) return Unauthorized();

        var comment = PackageAdvisorService.Clean(request.Comment, 1000);
        bool ok = await _store.SetFeedbackAsync(request.AdviceId, userId, request.Helpful ? 1 : -1, comment);
        return ok ? Ok() : NotFound(new { message = "Advies nie gevind nie." });
    }

    /// <summary>What is running: model version and evidence per offer kind, the configured LLM, the exchange rate. Shown transparently in the UI.</summary>
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        model = _model.Status,
        llm = new { provider = _llm.Provider, model = _llm.Model, configured = _llm.IsConfigured },
        usdToZarRate = _options.UsdToZarRate
    });

    // ------------------------------------------------------------------ admin: the learning loop

    /// <summary>Harvests real listings into aiMarketObservations right now (the background job does this on a schedule).</summary>
    [HttpPost("admin/harvest")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Harvest()
    {
        var result = await _store.HarvestObservationsAsync(_options.UsdToZarRate, _model.Catalog);
        return Ok(new { total = result.Total, byKind = result.UpsertedByKind, skipped = result.Skipped });
    }

    /// <summary>Hot-swaps a freshly retrained model bundle (after `train_price_model.py --promote`) without restarting the API.</summary>
    [HttpPost("admin/reload-models")]
    [Authorize(Roles = "Admin")]
    public IActionResult ReloadModels()
    {
        _model.Reload();
        var status = _model.Status;
        return status.Ready ? Ok(status) : StatusCode(500, new { message = status.Error });
    }

    /// <summary>
    /// Installs a retrained model bundle (a zip produced by tools/ai/retrain.py) on the RUNNING deployment: it is validated (safe
    /// zip, only known files, every ONNX model loads, the trainer's golden vectors reproduce), written to the persistent model
    /// volume - so it survives restarts and redeploys - and activated if it is newer than the one baked into the image.
    /// </summary>
    [HttpPost("admin/upload-model")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadModel()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        ms.Position = 0;
        var result = _model.InstallBundle(ms);
        return result.Ok ? Ok(result) : BadRequest(new { message = result.Message });
    }

    /// <summary>
    /// Returns the exact system prompt, user prompt and JSON schema the LLM would get for this request (plus the model's numbers)
    /// without calling any LLM or logging. tools/ai/llm/compare_providers.py uses it so provider comparisons run on the exact production prompts.
    /// </summary>
    [HttpPost("admin/dry-run")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DryRun([FromBody] AiOptimizeRequest request)
    {
        if (request == null) return BadRequest(new { message = "Versoek ontbreek." });
        try { return Ok(await _advisor.BuildPromptAsync(request, CallerId)); }
        catch (AiValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (AiModelUnavailableException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    /// <summary>All observations as JSONL - feed to `train_price_model.py --real observations.jsonl`.</summary>
    [HttpGet("admin/observations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ExportObservations()
        => File(Encoding.UTF8.GetBytes(string.Join('\n', await _store.ExportObservationsJsonlAsync()) + "\n"), "application/x-ndjson", "observations.jsonl");

    /// <summary>Anonymised advice pairs as JSONL (prompt context + answer + rating); minFeedback=1 keeps only advice users rated helpful.</summary>
    [HttpGet("admin/advice-log")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ExportAdviceLog([FromQuery] int? minFeedback = null)
        => File(Encoding.UTF8.GetBytes(string.Join('\n', await _store.ExportAdviceLogJsonlAsync(minFeedback)) + "\n"), "application/x-ndjson", "advice-log.jsonl");
}
