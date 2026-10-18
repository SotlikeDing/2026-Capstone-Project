using System.Text.Json;
using System.Text.Json.Serialization;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

/// <summary>
/// Pins the C# runtime to the Python trainer. The trainer writes golden_vectors.json (raw input, the exact feature vector it
/// computed, and the ONNX prediction). These tests replay those through FeatureEncoder and the real ONNX models. If either side's
/// feature logic drifts, a price would silently be wrong in production - this is what catches it.
/// </summary>
public class AiFeatureParityTests
{
    private sealed class Golden
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("raw")] public PriceRawInput Raw { get; set; } = new();
        [JsonPropertyName("features")] public List<double> Features { get; set; } = new();
        [JsonPropertyName("p50Usd")] public double P50Usd { get; set; }
    }

    private sealed class Spec { [JsonPropertyName("kinds")] public Dictionary<string, KindSpec> Kinds { get; set; } = new(); }
    private sealed class KindSpec { [JsonPropertyName("features")] public List<string> Features { get; set; } = new(); }

    private static List<Golden> LoadGolden() =>
        JsonSerializer.Deserialize<List<Golden>>(File.ReadAllText(Path.Combine(AiTestSupport.ModelDir(), "golden_vectors.json")))!;

    [Fact]
    public void GoldenVectors_CoverEveryKind()
    {
        var golden = LoadGolden();
        Assert.True(golden.Count >= 50);
        foreach (var kind in new[] { "PJ_PACKAGE", "PH_RATE", "VG_PACKAGE", "FARM_DAILY_RATE", "SPECIES_FEE" })
            Assert.Contains(golden, g => g.Kind == kind);
    }

    [Fact]
    public void Encoder_ReproducesPythonFeatureVectors()
    {
        var dir = AiTestSupport.ModelDir();
        var encoder = new FeatureEncoder(SpeciesCatalog.Load(Path.Combine(dir, "species_catalog.json")));
        var spec = JsonSerializer.Deserialize<Spec>(File.ReadAllText(Path.Combine(dir, "feature_spec.json")))!;

        foreach (var g in LoadGolden())
        {
            var named = encoder.Encode(g.Kind, g.Raw);
            var order = spec.Kinds[g.Kind].Features;
            Assert.Equal(order.Count, g.Features.Count);
            for (int i = 0; i < order.Count; i++)
            {
                Assert.True(named.ContainsKey(order[i]), $"{g.Kind}: encoder produced no feature '{order[i]}'");
                Assert.True(Math.Abs(named[order[i]] - g.Features[i]) < 1e-4,
                    $"{g.Kind}.{order[i]}: C# {named[order[i]]} != Python {g.Features[i]}");
            }
        }
    }

    [Fact]
    public void Onnx_PredictionsMatchPythonForEveryGoldenCase()
    {
        using var model = AiTestSupport.RealModel();
        Assert.True(model.IsReady, model.Status.Error);

        foreach (var g in LoadGolden())
        {
            var est = model.Predict(g.Kind, g.Raw);
            double rel = Math.Abs(est.P50Usd - g.P50Usd) / g.P50Usd;
            Assert.True(rel < 0.005, $"{g.Kind}: C# {est.P50Usd:F2} vs Python {g.P50Usd:F2} ({rel:P2} apart)");
        }
    }

    [Theory]
    [InlineData("Limpopo", "limpopo")]
    [InlineData("  NOORD-KAAP ", "northern_cape")]
    [InlineData("KwaZulu-Natal", "kwazulu_natal")]
    [InlineData("Vrystaat", "free_state")]
    [InlineData("Noord-Wes", "north_west")]
    [InlineData("north_west", "north_west")]
    [InlineData("Atlantis", "limpopo")]
    [InlineData(null, "limpopo")]
    public void NormalizeProvince_HandlesAfrikaansEnglishAndUnknown(string? input, string expected)
        => Assert.Equal(expected, SpeciesCatalog.NormalizeProvince(input));

    [Theory]
    [InlineData("Koedoe", "kudu")]
    [InlineData("KUDU", "kudu")]
    [InlineData("Wildebees", "blue_wildebeest")]
    [InlineData("Rooibok", "impala")]
    [InlineData("Buffel", "buffalo")]
    [InlineData("Leeu", null)]
    [InlineData("Olifant", null)]
    public void SpeciesResolution_HandlesBothSpellingsUsedInTheApp(string name, string? expectedId)
    {
        var catalog = SpeciesCatalog.Load(Path.Combine(AiTestSupport.ModelDir(), "species_catalog.json"));
        Assert.Equal(expectedId, catalog.Resolve(name)?.Id);
    }
}
