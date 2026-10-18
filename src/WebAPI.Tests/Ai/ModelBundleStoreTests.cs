using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

/// <summary>
/// The persistent-model behaviour that makes a retrained model survive deployments: bundle in the image + optional bundle in a
/// volume, newest training timestamp wins, every bundle integrity-checked, uploads validated. All against the real ONNX bundle.
/// </summary>
public class ModelBundleStoreTests : IDisposable
{
    private readonly List<string> _temp = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mikpunt-ai-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _temp.Add(d);
        return d;
    }

    public void Dispose()
    {
        foreach (var d in _temp)
            try { Directory.Delete(d, true); } catch { /* best effort */ }
    }

    private static string Stamp(int year) => $"{year}-01-01T00:00:00+00:00";

    /// <summary>Copies the committed bundle, optionally retagging its version/timestamp and tampering with it.</summary>
    private void WriteBundle(string dir, string version, int year, Action<string>? tamper = null)
    {
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(AiTestSupport.ModelDir()))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);

        var specPath = Path.Combine(dir, "feature_spec.json");
        var spec = JsonNode.Parse(File.ReadAllText(specPath))!;
        spec["modelVersion"] = version;
        spec["trainedAtUtc"] = Stamp(year);
        File.WriteAllText(specPath, spec.ToJsonString());
        tamper?.Invoke(dir);
    }

    private PriceModelService Service(string? volumeRoot) => new(
        Microsoft.Extensions.Options.Options.Create(new AiHelperOptions { ModelDirectory = AiTestSupport.ModelDir(), ModelOverrideDirectory = volumeRoot ?? "" }),
        NullLogger<PriceModelService>.Instance);

    private MemoryStream Zip(string version, int year, Action<string>? tamper = null, Action<ZipArchive>? extra = null)
    {
        var dir = Path.Combine(TempDir(), "b");
        WriteBundle(dir, version, year, tamper);
        var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in Directory.GetFiles(dir)) za.CreateEntryFromFile(f, Path.GetFileName(f));
            extra?.Invoke(za);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void WithoutAVolume_TheImageBundleIsUsed()
    {
        using var svc = Service(null);
        Assert.True(svc.IsReady);
        Assert.Equal("image", svc.Status.Source);
    }

    [Fact]
    public void ANewerVolumeBundle_WinsOverTheImage()
    {
        var root = TempDir();
        WriteBundle(Path.Combine(root, "current"), "retrained-2030", 2030);
        using var svc = Service(root);
        Assert.Equal("retrained-2030", svc.Status.Version);
        Assert.Equal("volume", svc.Status.Source);
    }

    [Fact]
    public void AStaleVolumeBundle_LosesToAFresherImage()
    {
        var root = TempDir();
        WriteBundle(Path.Combine(root, "current"), "old-2020", 2020);
        using var svc = Service(root);
        Assert.Equal("image", svc.Status.Source);          // a redeploy with a newer baked model is never beaten by an old volume
        Assert.NotEqual("old-2020", svc.Status.Version);
    }

    [Fact]
    public void ACorruptVolumeBundle_FallsBackToTheImage_WithoutCrashing()
    {
        var root = TempDir();
        WriteBundle(Path.Combine(root, "current"), "corrupt-2030", 2030, d => File.WriteAllBytes(Path.Combine(d, "pj_package.onnx"), new byte[] { 1, 2, 3, 4 }));
        using var svc = Service(root);
        Assert.True(svc.IsReady);
        Assert.Equal("image", svc.Status.Source);
    }

    [Fact]
    public void ABundleWhoseGoldenVectorsDoNotReproduce_IsRejected()
    {
        // Simulates a bundle trained with a different feature contract than this code implements: its prices would be silently wrong.
        var root = TempDir();
        WriteBundle(Path.Combine(root, "current"), "drifted-2030", 2030, d =>
        {
            var p = Path.Combine(d, "golden_vectors.json");
            var arr = JsonNode.Parse(File.ReadAllText(p))!.AsArray();
            foreach (var c in arr) c!["p50Usd"] = c["p50Usd"]!.GetValue<double>() * 1.5;
            File.WriteAllText(p, arr.ToJsonString());
        });
        using var svc = Service(root);
        Assert.Equal("image", svc.Status.Source);
    }

    [Fact]
    public void Install_ValidNewerBundle_IsActivated_AndSurvivesARestart()
    {
        var root = TempDir();
        using (var svc = Service(root))
        {
            var res = svc.InstallBundle(Zip("uploaded-2031", 2031));
            Assert.True(res.Ok, res.Message);
            Assert.True(res.Activated);
            Assert.Equal("uploaded-2031", svc.Status.Version);
            Assert.Equal("volume", svc.Status.Source);
            Assert.True(File.Exists(Path.Combine(root, "current", "feature_spec.json")));
            Assert.DoesNotContain(Directory.GetDirectories(root), d => Path.GetFileName(d).StartsWith(".staging"));   // no leftovers
        }
        using var restarted = Service(root);                       // a new process / container on the same volume
        Assert.Equal("uploaded-2031", restarted.Status.Version);
        Assert.Equal("volume", restarted.Status.Source);
    }

    [Fact]
    public void Install_OlderThanTheImage_IsStoredButNotActivated_AndSaysSo()
    {
        var root = TempDir();
        using var svc = Service(root);
        var res = svc.InstallBundle(Zip("ancient-2020", 2020));
        Assert.True(res.Ok);
        Assert.False(res.Activated);
        Assert.Equal("image", res.ActiveSource);
        Assert.Contains("nuwer", res.Message);
    }

    [Fact]
    public void Install_ReplacesThePreviousVolumeBundle()
    {
        var root = TempDir();
        using var svc = Service(root);
        Assert.True(svc.InstallBundle(Zip("first-2031", 2031)).Activated);
        Assert.True(svc.InstallBundle(Zip("second-2032", 2032)).Activated);
        Assert.Equal("second-2032", svc.Status.Version);
    }

    [Fact]
    public void Install_ZipSlipPath_IsRejected_AndNothingIsWrittenOutsideOrReplaced()
    {
        var root = TempDir();
        using var svc = Service(root);
        Assert.True(svc.InstallBundle(Zip("good-2031", 2031)).Activated);

        var evil = Zip("evil-2040", 2040, extra: za => za.CreateEntry("../../evil.onnx"));
        var res = svc.InstallBundle(evil);

        Assert.False(res.Ok);
        Assert.Equal("good-2031", svc.Status.Version);                                  // the working bundle is untouched
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "evil.onnx")));
        Assert.DoesNotContain(Directory.GetDirectories(root), d => Path.GetFileName(d).StartsWith(".staging"));
    }

    [Theory]
    [InlineData("payload.dll")]
    [InlineData("sub/feature_spec.json")]
    [InlineData("Feature_Spec.json")]
    public void Install_UnknownOrNestedFiles_AreRejected(string name)
    {
        using var svc = Service(TempDir());
        var res = svc.InstallBundle(Zip("x-2031", 2031, extra: za => za.CreateEntry(name)));
        Assert.False(res.Ok);
        Assert.Contains("Onverwagte", res.Message);
    }

    [Fact]
    public void Install_CorruptOnnx_IsRejected_AndTheCurrentBundleIsKept()
    {
        var root = TempDir();
        using var svc = Service(root);
        Assert.True(svc.InstallBundle(Zip("good-2031", 2031)).Activated);

        var res = svc.InstallBundle(Zip("bad-2040", 2040, d => File.WriteAllBytes(Path.Combine(d, "vg_package.onnx"), new byte[] { 9, 9, 9 })));

        Assert.False(res.Ok);
        Assert.Equal("good-2031", svc.Status.Version);
    }

    [Fact]
    public void Install_GoldenVectorMismatch_IsRejected()
    {
        using var svc = Service(TempDir());
        var res = svc.InstallBundle(Zip("drift-2031", 2031, d =>
        {
            var p = Path.Combine(d, "golden_vectors.json");
            var arr = JsonNode.Parse(File.ReadAllText(p))!.AsArray();
            foreach (var c in arr) c!["p50Usd"] = c["p50Usd"]!.GetValue<double>() * 2;
            File.WriteAllText(p, arr.ToJsonString());
        }));
        Assert.False(res.Ok);
        Assert.Contains("Golden", res.Message);
        Assert.Equal("image", svc.Status.Source);
    }

    [Fact]
    public void Install_TooManyFiles_AndNotAZip_AreRejected()
    {
        using var svc = Service(TempDir());
        var many = Zip("many-2031", 2031, extra: za => { for (int i = 0; i < 40; i++) za.CreateEntry($"m{i}.onnx"); });
        Assert.False(svc.InstallBundle(many).Ok);
        Assert.False(svc.InstallBundle(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })).Ok);
    }

    [Fact]
    public void Install_WithoutAConfiguredVolume_ExplainsWhatToDo()
    {
        using var svc = Service(null);
        var res = svc.InstallBundle(Zip("x-2031", 2031));
        Assert.False(res.Ok);
        Assert.Contains("ModelOverrideDirectory", res.Message);
    }
}
