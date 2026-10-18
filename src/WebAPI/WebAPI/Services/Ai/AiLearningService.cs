using Microsoft.Extensions.Options;
using WebAPI.Database;

namespace WebAPI.Services.Ai;

/// <summary>
/// The "learn from the system" half of the AI Helper. On a schedule it harvests the platform's real listings (packages, PH day
/// rates, farm daily rates, per-species prices) into aiMarketObservations. Nothing is retrained inside the web app: an admin
/// downloads the observations (GET api/aihelper/admin/observations), runs tools/ai/train_price_model.py --real ... --promote,
/// and reloads the model (POST api/aihelper/admin/reload-models). The trainer blends real rows into the bootstrap data, shifting
/// weight to real data as it accumulates, and reports how wrong the bootstrap assumptions turned out to be.
/// </summary>
public sealed class AiLearningService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IPriceModel _model;
    private readonly AiHelperOptions _options;
    private readonly ILogger<AiLearningService> _logger;

    public AiLearningService(IServiceScopeFactory scopes, IPriceModel model, IOptions<AiHelperOptions> options, ILogger<AiLearningService> logger)
    {
        _scopes = scopes;
        _model = model;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Learning.HarvestIntervalHours <= 0) return;

        try
        {
            // Let the app (and the DB bootstrapper) finish starting before the first harvest.
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IAiHelperStore>();
                    var result = await store.HarvestObservationsAsync(_options.UsdToZarRate, _model.Catalog);
                    _logger.LogInformation("[AI LEARNING] Harvested {Total} observation(s): {Detail}{Skipped}",
                        result.Total, string.Join(", ", result.UpsertedByKind.Select(k => $"{k.Key}={k.Value}")),
                        result.Skipped.Count > 0 ? " | skipped: " + string.Join("; ", result.Skipped) : "");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[AI LEARNING] Harvest failed: {Message}", ex.Message);
                }
                await Task.Delay(TimeSpan.FromHours(_options.Learning.HarvestIntervalHours), stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
