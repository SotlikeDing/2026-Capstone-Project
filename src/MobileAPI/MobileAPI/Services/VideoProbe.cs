using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MobileApi.Options;

namespace MobileApi.Services;

public sealed record VideoProbeResult(int Width, int Height, decimal DurationSeconds);

public interface IVideoProbe
{
    /// <summary>Returns the first video stream's size and the clip duration, or null when the file is not a decodable video.</summary>
    Task<VideoProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Reads clip metadata with ffprobe (installed alongside ffmpeg in the MobileAPI image).</summary>
public sealed class FfprobeVideoProbe : IVideoProbe
{
    private readonly SharedBookingOptions _options;
    public FfprobeVideoProbe(IOptions<SharedBookingOptions> options) => _options = options.Value;

    public async Task<VideoProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_options.FfprobeExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-v", "error", "-select_streams", "v:0",
                     "-show_entries", "stream=width,height,duration:format=duration",
                     "-of", "json", path
                 })
            start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Process? process;
        try { process = Process.Start(start); }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("ffprobe is nie op die bediener beskikbaar nie."); }
        if (process is null) return null;
        using (process)
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var json = await outputTask;
                _ = await errorTask;
                return process.ExitCode == 0 ? Parse(json) : null;
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
        }
    }

    /// <summary>Parses `ffprobe -of json` output; prefers the container duration and falls back to the stream's.</summary>
    public static VideoProbeResult? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() == 0)
                return null;
            var stream = streams[0];
            var width = stream.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 0;
            var height = stream.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 0;
            var duration = (root.TryGetProperty("format", out var format) ? ReadDuration(format) : null)
                ?? ReadDuration(stream) ?? 0m;
            return new VideoProbeResult(width, height, duration);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? ReadDuration(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("duration", out var value) &&
        value.ValueKind == JsonValueKind.String &&
        decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
}
