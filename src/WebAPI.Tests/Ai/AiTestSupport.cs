using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

/// <summary>Shared fakes/helpers for the AI Helper tests. The real trained model bundle is used wherever numbers matter; only the LLM and the database are faked.</summary>
internal static class AiTestSupport
{
    /// <summary>Finds src/WebAPI/WebAPI/AiModels/package_price by walking up from the test binaries, so the tests always exercise the committed bundle.</summary>
    public static string ModelDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "WebAPI", "WebAPI", "AiModels", "package_price");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("AiModels/package_price not found above " + AppContext.BaseDirectory);
    }

    public static AiHelperOptions Options() => new() { ModelDirectory = ModelDir(), UsdToZarRate = 18.0m };

    public static PriceModelService RealModel() =>
        new(Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<PriceModelService>.Instance);

    public static PackageAdvisorService Advisor(ILlmClient llm, IAiHelperStore? store = null, IPriceModel? model = null) =>
        new(model ?? RealModel(), llm, store ?? new FakeStore(), Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<PackageAdvisorService>.Instance);

    public const string GoodNarrativeJson = """
        {"headline":"Jou prys is in lyn met die mark.","titleSuggestions":["Limpopo Trofee Safari","Bosveld Koedoe Ekspedisie","Waterberg Jagpakket","Vierde titel"],
         "descriptionRewrite":"'n Volledige jagervaring.",
         "inclusionSuggestions":[{"item":"Lughawe-vervoer","why":"Verwyder wrywing.","impact":"hoog"},{"item":"Vel-voorbereiding","why":"Verwag deur jagters.","impact":"onbekend"}],
         "upsells":[{"name":"Hommeltuig-video","why":"Meer beelde."}],
         "seasonalAdvice":"Winter is topseisoen.",
         "tips":[{"title":"Laag prioriteit","detail":"x","priority":9},{"title":"Hoog prioriteit","detail":"y","priority":1}],
         "risks":["Onsekere skatting."]}
        """;
}

internal sealed class FakeLlm : ILlmClient
{
    public Func<string, string, Task<LlmResult>>? Handler { get; set; }
    public string? LastSystem { get; private set; }
    public string? LastUser { get; private set; }
    public int Calls { get; private set; }
    public string Provider => "Fake";
    public string Model => "fake-1";
    public bool IsConfigured { get; set; } = true;

    public static FakeLlm Returning(string json) => new() { Handler = (_, _) => Task.FromResult(new LlmResult(json, "Fake", "fake-1", 42)) };
    public static FakeLlm Failing(string message = "kan nie bereik nie") => new() { Handler = (_, _) => throw new LlmException(message) };

    public Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct)
    {
        Calls++;
        LastSystem = systemPrompt;
        LastUser = userPrompt;
        return Handler!(systemPrompt, userPrompt);
    }
}

internal sealed class FakeStore : IAiHelperStore
{
    public List<AdviceLogEntry> Logged { get; } = new();
    public AiAdBenchmark AdBenchmark { get; set; } = new();
    public bool ThrowOnComparables { get; set; }
    public bool ThrowOnLog { get; set; }
    public (int, long, bool) Feedback { get; private set; }

    public Task<List<AiComparable>> GetPjComparablesAsync(string provinceId, int take)
    {
        if (ThrowOnComparables) throw new InvalidOperationException("db down");
        return Task.FromResult(new List<AiComparable> { new() { Kind = "PJ_PACKAGE", Title = "Kudu Bosveld Pakket", Price = 1500m, DurationDays = 4, Province = "Limpopo" } });
    }
    public Task<List<AiComparable>> GetVgComparablesAsync(int take) => Task.FromResult(new List<AiComparable>());
    public Task<Dictionary<string, (int Count, double Avg, double Min, double Max)>> GetSpeciesPriceStatsAsync() =>
        Task.FromResult(new Dictionary<string, (int, double, double, double)>(StringComparer.OrdinalIgnoreCase) { ["Kudu"] = (2, 8500, 8000, 9000) });
    public Task<AiAdBenchmark> GetAdBenchmarkAsync(int userId, string? category) => Task.FromResult(AdBenchmark);
    public Task<long> InsertAdviceAsync(AdviceLogEntry entry)
    {
        if (ThrowOnLog) throw new InvalidOperationException("log table missing");
        Logged.Add(entry);
        return Task.FromResult((long)Logged.Count);
    }
    public Task<bool> SetFeedbackAsync(long adviceId, int userId, int feedback, string? comment)
    {
        Feedback = (userId, adviceId, feedback > 0);
        return Task.FromResult(adviceId == 1);
    }
    public Task<HarvestResult> HarvestObservationsAsync(decimal usdToZar, SpeciesCatalog catalog) => Task.FromResult(new HarvestResult());
    public Task<List<string>> ExportObservationsJsonlAsync() => Task.FromResult(new List<string>());
    public Task<List<string>> ExportAdviceLogJsonlAsync(int? minFeedback) => Task.FromResult(new List<string>());
}

/// <summary>A price model that failed to load - used to prove the API answers 503 (or, for AP, does not need it).</summary>
internal sealed class NotReadyModel : IPriceModel
{
    public bool IsReady => false;
    public PriceModelStatus Status => new() { Ready = false, Error = "bundle ontbreek" };
    public SpeciesCatalog Catalog => throw new AiModelUnavailableException("bundle ontbreek");
    public JsonElement? MarketFacts => null;
    public PriceEstimate Predict(string kind, PriceRawInput raw) => throw new AiModelUnavailableException("bundle ontbreek");
    public void Reload() { }
    public ModelInstallResult InstallBundle(Stream zip) => new(false, "nie beskikbaar");
}
