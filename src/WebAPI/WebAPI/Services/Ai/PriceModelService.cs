using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WebAPI.Services.Ai;

/// <summary>Thrown when the trained model bundle is missing or unreadable - the API answers 503 instead of guessing a price.</summary>
public class AiModelUnavailableException : Exception
{
    public AiModelUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A scored price with its uncertainty band, all in USD (the models' native currency).</summary>
/// <param name="Evidence">"published" (anchored on published price lists), "real+published" (real MikPunt data blended in) or "assumed" (no published source).</param>
/// <param name="Confidence">"high" | "medium" | "low" - derived from the evidence and the market segment; never inflated.</param>
public sealed record PriceEstimate(string Kind, double P10Usd, double P50Usd, double P90Usd, string Evidence, string Confidence, string ModelVersion);

/// <summary>Machine-readable description of the loaded bundle, exposed via api/aihelper/status and shown (transparently) in the UI.</summary>
public sealed class PriceModelStatus
{
    public bool Ready { get; init; }
    public string? Error { get; init; }
    public string? Version { get; init; }
    public DateTime? TrainedAtUtc { get; init; }
    /// <summary>"image" (baked into the deployed image) or "volume" (a retrained bundle in the persistent model folder).</summary>
    public string? Source { get; init; }
    public Dictionary<string, KindStatus> Kinds { get; init; } = new();
    public sealed record KindStatus(string Evidence, int NReal, int NSynthetic);
}

/// <param name="Ok">The uploaded bundle passed every check and was written to the persistent folder.</param>
/// <param name="Activated">It is now the bundle in use (a bundle older than the one baked into the image is stored but not used).</param>
public sealed record ModelInstallResult(bool Ok, string Message, string? UploadedVersion = null, bool Activated = false, string? ActiveVersion = null, string? ActiveSource = null);

/// <summary>Abstraction over the price model so the advisor can be unit-tested without ONNX files.</summary>
public interface IPriceModel
{
    bool IsReady { get; }
    PriceModelStatus Status { get; }
    SpeciesCatalog Catalog { get; }
    /// <summary>The published market facts (tools/ai/data/market_facts.json) shipped with the bundle, used to ground the LLM. Null when the model is not loaded.</summary>
    JsonElement? MarketFacts { get; }
    PriceEstimate Predict(string kind, PriceRawInput raw);
    /// <summary>Re-reads the bundles from disk and activates the newest valid one.</summary>
    void Reload();
    /// <summary>Validates a zipped bundle (as produced by tools/ai/retrain.py), stores it in the persistent model folder and activates it if it is the newest.</summary>
    ModelInstallResult InstallBundle(Stream zip);
}

/// <summary>
/// Loads the trained price-model bundle (ONNX gradient-boosted regressors, one per offer kind) and scores offers in-process.
/// Each model predicts log(price in USD); the P10/P90 band is the trainer's held-out residual quantiles applied in log space
/// (split-conformal style), so the band is calibrated rather than invented.
///
/// Two places can hold a bundle: the one baked into the image (AiHelper:ModelDirectory) and an optional persistent folder
/// (AiHelper:ModelOverrideDirectory, a Docker volume in deployment) that a retrained bundle is uploaded to. The bundle with the
/// newer training timestamp is used, so a retrained model survives redeploys, and a fresh image beats a stale volume.
/// Every bundle is integrity-checked on load by replaying the trainer's golden vectors through the real ONNX models.
/// </summary>
public sealed class PriceModelService : IPriceModel, IDisposable
{
    private const int GoldenChecks = 12;
    private const double GoldenTolerance = 0.01;
    private const long MaxFileBytes = 20L * 1024 * 1024;
    private const long MaxBundleBytes = 60L * 1024 * 1024;

    private static readonly HashSet<string> AllowedFiles = new(StringComparer.Ordinal)
    {
        "feature_spec.json", "species_catalog.json", "market_facts.json", "model_card.json", "golden_vectors.json"
    };

    private readonly AiHelperOptions _options;
    private readonly ILogger<PriceModelService> _logger;
    private readonly object _gate = new();
    private volatile Bundle? _bundle;
    private string? _loadError;

    public PriceModelService(IOptions<AiHelperOptions> options, ILogger<PriceModelService> logger)
    {
        _options = options.Value;
        _logger = logger;
        Reload();
    }

    private string ImageDir => Path.IsPathRooted(_options.ModelDirectory)
        ? _options.ModelDirectory
        : Path.Combine(AppContext.BaseDirectory, _options.ModelDirectory);

    private string? VolumeRoot => string.IsNullOrWhiteSpace(_options.ModelOverrideDirectory) ? null : _options.ModelOverrideDirectory;

    public bool IsReady => _bundle is not null;

    public SpeciesCatalog Catalog => _bundle?.Catalog ?? throw new AiModelUnavailableException(_loadError ?? "Die prysmodel is nie gelaai nie.");

    public JsonElement? MarketFacts => _bundle?.MarketFacts;

    public PriceModelStatus Status
    {
        get
        {
            var b = _bundle;
            if (b is null) return new PriceModelStatus { Ready = false, Error = _loadError };
            return new PriceModelStatus
            {
                Ready = true,
                Version = b.Spec.ModelVersion,
                TrainedAtUtc = b.Spec.TrainedAtUtc,
                Source = b.Source,
                Kinds = b.Spec.Kinds.ToDictionary(k => k.Key, k => new PriceModelStatus.KindStatus(k.Value.Evidence, k.Value.NReal, k.Value.NSynthetic))
            };
        }
    }

    /// <summary>
    /// Loads every available bundle and activates the newest valid one. A bundle that fails to load or fails the golden-vector
    /// check is skipped (and logged); if none is valid, the previously active bundle (if any) stays in use.
    /// </summary>
    public void Reload()
    {
        lock (_gate)
        {
            var candidates = new List<(string Source, string Dir)> { ("image", ImageDir) };
            if (VolumeRoot is { } root) candidates.Add(("volume", Path.Combine(root, "current")));

            Bundle? best = null;
            var errors = new List<string>();
            foreach (var (source, dir) in candidates)
            {
                if (!Directory.Exists(dir))
                {
                    if (source == "image") errors.Add($"{source}: {dir} bestaan nie");
                    continue;
                }
                try
                {
                    var b = LoadBundle(dir, source);
                    if (best is null || (b.Spec.TrainedAtUtc ?? DateTime.MinValue) > (best.Spec.TrainedAtUtc ?? DateTime.MinValue))
                    {
                        best?.Dispose();
                        best = b;
                    }
                    else
                    {
                        b.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{source}: {ex.Message}");
                    _logger.LogError(ex, "[AI] Could not load the {Source} price model bundle from {Dir}.", source, dir);
                }
            }

            if (best is null)
            {
                _loadError = "Prysmodel kon nie gelaai word nie: " + string.Join("; ", errors);
                return;
            }

            // The previous bundle is intentionally not disposed: an in-flight request may still be scoring on it. It is
            // finalised by the GC once unreferenced. (Models are read from memory, so no file stays locked.)
            _bundle = best;
            _loadError = null;
            _logger.LogInformation("[AI] Price model {Version} active from {Source} ({Dir}), trained {Trained:u}.",
                best.Spec.ModelVersion, best.Source, best.Dir, best.Spec.TrainedAtUtc);
        }
    }

    public ModelInstallResult InstallBundle(Stream zip)
    {
        if (VolumeRoot is not { } root)
            return new ModelInstallResult(false, "Geen persistente modelgids is opgestel nie (AiHelper:ModelOverrideDirectory). Bou die model in die beeld in en herontplooi.");

        lock (_gate)
        {
            string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(staging);
                ExtractSafely(zip, staging);

                // Full validation before anything is replaced: spec, catalogue, every ONNX session and the golden-vector replay.
                using var candidate = LoadBundle(staging, "volume");
                string uploadedVersion = candidate.Spec.ModelVersion;

                string current = Path.Combine(root, "current"), previous = Path.Combine(root, ".previous");
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
                if (Directory.Exists(current)) Directory.Move(current, previous);
                Directory.Move(staging, current);
                staging = "";

                Reload();
                var active = _bundle;
                bool activated = active is { Source: "volume" } && active.Spec.ModelVersion == uploadedVersion;
                string msg = activated
                    ? $"Model {uploadedVersion} is gestoor en aktief."
                    : $"Model {uploadedVersion} is gestoor, maar die model in gebruik ({active?.Spec.ModelVersion}) is nuwer opgelei en bly aktief.";
                _logger.LogInformation("[AI] Uploaded bundle {Version}: {Message}", uploadedVersion, msg);
                return new ModelInstallResult(true, msg, uploadedVersion, activated, active?.Spec.ModelVersion, active?.Source);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or IOException
                                          or OnnxRuntimeException or KeyNotFoundException or ArgumentException)
            {
                _logger.LogWarning("[AI] Rejected uploaded model bundle: {Message}", ex.Message);
                return new ModelInstallResult(false, "Modelbundel verwerp: " + ex.Message);
            }
            finally
            {
                if (staging.Length > 0 && Directory.Exists(staging))
                    try { Directory.Delete(staging, true); } catch { /* best effort */ }
            }
        }
    }

    public PriceEstimate Predict(string kind, PriceRawInput raw)
    {
        var b = _bundle ?? throw new AiModelUnavailableException(_loadError ?? "Die prysmodel is nie gelaai nie.");
        return Score(b, kind, raw);
    }

    public void Dispose() => _bundle?.Dispose();

    // ================================================================== loading / validation

    private static Bundle LoadBundle(string dir, string source)
    {
        var spec = JsonSerializer.Deserialize<FeatureSpec>(File.ReadAllText(Path.Combine(dir, "feature_spec.json")))
                   ?? throw new InvalidDataException("feature_spec.json is empty.");
        if (spec.SchemaVersion != 1) throw new InvalidDataException($"Unsupported feature spec schema {spec.SchemaVersion}.");
        if (spec.Kinds.Count == 0) throw new InvalidDataException("feature_spec.json has no model kinds.");

        var catalog = SpeciesCatalog.Load(Path.Combine(dir, "species_catalog.json"));
        JsonElement? facts = null;
        var factsPath = Path.Combine(dir, "market_facts.json");
        if (File.Exists(factsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(factsPath));
            facts = doc.RootElement.Clone();
        }

        var sessions = new Dictionary<string, InferenceSession>();
        try
        {
            foreach (var (kind, ks) in spec.Kinds)
            {
                if (ks.Onnx.Contains('/') || ks.Onnx.Contains('\\') || ks.Onnx.Contains(".."))
                    throw new InvalidDataException($"Invalid onnx file name '{ks.Onnx}'.");
                // Loaded from memory (not from the path) so no file handle stays open: a bundle folder can then be replaced on any OS.
                sessions[kind] = new InferenceSession(File.ReadAllBytes(Path.Combine(dir, ks.Onnx)));
            }
            var bundle = new Bundle(spec, catalog, new FeatureEncoder(catalog), sessions, facts, source, dir);
            VerifyGolden(bundle, dir);
            return bundle;
        }
        catch
        {
            foreach (var s in sessions.Values) s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Replays the trainer's golden vectors (raw input -> expected price) through this bundle. A mismatch means the bundle was
    /// trained with a different feature contract than this code implements, so its prices would silently be wrong: reject it.
    /// </summary>
    private static void VerifyGolden(Bundle b, string dir)
    {
        var path = Path.Combine(dir, "golden_vectors.json");
        if (!File.Exists(path)) return;

        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(path)) ?? new();
        foreach (var g in cases.Where(c => b.Spec.Kinds.ContainsKey(c.Kind)).Take(GoldenChecks))
        {
            double actual = Score(b, g.Kind, g.Raw).P50Usd;
            if (g.P50Usd <= 0 || Math.Abs(actual - g.P50Usd) / g.P50Usd > GoldenTolerance)
                throw new InvalidDataException($"Golden-vector mismatch for {g.Kind}: expected {g.P50Usd:F2}, got {actual:F2} (feature contract differs).");
        }
    }

    private static PriceEstimate Score(Bundle b, string kind, PriceRawInput raw)
    {
        if (!b.Spec.Kinds.TryGetValue(kind, out var ks)) throw new ArgumentException($"Unknown price kind '{kind}'.", nameof(kind));

        var named = b.Encoder.Encode(kind, raw);
        var vector = new float[ks.Features.Count];
        for (int i = 0; i < vector.Length; i++)
            vector[i] = named.TryGetValue(ks.Features[i], out var v) ? (float)v : 0f;

        using var results = b.Sessions[kind].Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(vector, new[] { 1, vector.Length }))
        });
        double logPred = results.First().AsEnumerable<float>().First();

        double p50 = Math.Exp(logPred);
        double p10 = p50 * Math.Exp(ks.BandLog.Lo);
        double p90 = p50 * Math.Exp(ks.BandLog.Hi);

        bool domestic = string.Equals(raw.Market, "domestic", StringComparison.OrdinalIgnoreCase);
        string confidence = ks.Evidence switch { "real+published" => "high", "published" => "medium", _ => "low" };
        if (domestic && b.Spec.AssumedMarkets.Contains("domestic", StringComparer.OrdinalIgnoreCase)) confidence = "low";

        return new PriceEstimate(kind, p10, p50, p90, ks.Evidence, confidence, b.Spec.ModelVersion);
    }

    /// <summary>
    /// Extracts an uploaded bundle into `target`, accepting ONLY the known flat file names (no directories, no path segments, no
    /// unknown files) with per-file and total size caps - so a malicious or corrupt zip can neither escape the folder nor fill the disk.
    /// </summary>
    private static void ExtractSafely(Stream zip, string target)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        if (archive.Entries.Count == 0 || archive.Entries.Count > 32) throw new InvalidDataException("Die zip het 'n ongeldige aantal lêers.");

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            bool onnx = System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9_]{1,64}\.onnx$");
            if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || (!onnx && !AllowedFiles.Contains(name)))
                throw new InvalidDataException($"Onverwagte lêer in die bundel: '{name}'.");

            using var input = entry.Open();
            using var output = File.Create(Path.Combine(target, name));
            var buffer = new byte[81920];
            long written = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += n;
                total += n;
                if (written > MaxFileBytes || total > MaxBundleBytes) throw new InvalidDataException("Die bundel is te groot.");
                output.Write(buffer, 0, n);
            }
        }
    }

    private sealed class Bundle : IDisposable
    {
        public FeatureSpec Spec { get; }
        public SpeciesCatalog Catalog { get; }
        public FeatureEncoder Encoder { get; }
        public Dictionary<string, InferenceSession> Sessions { get; }
        public JsonElement? MarketFacts { get; }
        public string Source { get; }
        public string Dir { get; }

        public Bundle(FeatureSpec spec, SpeciesCatalog catalog, FeatureEncoder encoder, Dictionary<string, InferenceSession> sessions,
            JsonElement? facts, string source, string dir)
        {
            Spec = spec; Catalog = catalog; Encoder = encoder; Sessions = sessions; MarketFacts = facts; Source = source; Dir = dir;
        }

        public void Dispose()
        {
            foreach (var s in Sessions.Values) s.Dispose();
        }
    }

    // ---- feature_spec.json ----
    private sealed class FeatureSpec
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("modelVersion")] public string ModelVersion { get; set; } = "";
        [JsonPropertyName("trainedAtUtc")] public DateTime? TrainedAtUtc { get; set; }
        [JsonPropertyName("assumedMarkets")] public List<string> AssumedMarkets { get; set; } = new();
        [JsonPropertyName("kinds")] public Dictionary<string, KindSpec> Kinds { get; set; } = new();
    }

    private sealed class KindSpec
    {
        [JsonPropertyName("onnx")] public string Onnx { get; set; } = "";
        [JsonPropertyName("features")] public List<string> Features { get; set; } = new();
        [JsonPropertyName("bandLog")] public Band BandLog { get; set; } = new();
        [JsonPropertyName("evidence")] public string Evidence { get; set; } = "assumed";
        [JsonPropertyName("nReal")] public int NReal { get; set; }
        [JsonPropertyName("nSynthetic")] public int NSynthetic { get; set; }
    }

    private sealed class Band
    {
        [JsonPropertyName("lo")] public double Lo { get; set; }
        [JsonPropertyName("hi")] public double Hi { get; set; }
    }

    private sealed class GoldenCase
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("raw")] public PriceRawInput Raw { get; set; } = new();
        [JsonPropertyName("p50Usd")] public double P50Usd { get; set; }
    }
}
