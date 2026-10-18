using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebAPI.Services.Ai;

/// <summary>An LLM call failed (unreachable, timeout, HTTP error, rate limit, malformed body). The advisor degrades to numbers-only instead of failing the request.</summary>
public class LlmException : Exception
{
    public LlmException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <param name="Json">The model's answer: a JSON object meant to match the requested schema (the caller still validates it).</param>
public sealed record LlmResult(string Json, string Provider, string Model, long LatencyMs);

/// <summary>A hosted language model, reached with an API key, that answers with JSON for a schema.</summary>
public interface ILlmClient
{
    string Provider { get; }
    string Model { get; }
    bool IsConfigured { get; }
    Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct);
}

/// <summary>Used when AiHelper:Llm:Provider is "None" or no API key is set: the helper then returns the price model's numbers only.</summary>
public sealed class NullLlmClient : ILlmClient
{
    private readonly string _reason;

    public NullLlmClient(string? reason = null) => _reason = reason ?? "Geen taalmodel is opgestel nie (AiHelper:Llm:Provider = None).";

    public string Provider => "None";
    public string Model => "";
    public bool IsConfigured => false;

    public Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct)
        => throw new LlmException(_reason);
}

/// <summary>How a provider is asked for JSON. "json_schema" = schema-constrained output; "json_object" = valid JSON only, the schema goes in the prompt.</summary>
public sealed record LlmPreset(string Name, string BaseUrl, string DefaultModel, string JsonMode, string KeyUrl, JsonObject? ExtraBody = null);

/// <summary>
/// The hosted APIs the AI Helper knows by name. All but Anthropic speak the OpenAI chat-completions format. Default models are
/// a starting point - providers rename and retire models, so each one can be overridden with AiHelper:Llm:Model.
/// </summary>
public static class LlmPresets
{
    public static readonly IReadOnlyDictionary<string, LlmPreset> All = new Dictionary<string, LlmPreset>(StringComparer.OrdinalIgnoreCase)
    {
        // Google AI Studio. Free tier, no card. Thinking is kept low: it counts against max_tokens and is not needed here.
        ["Gemini"] = new("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-flash-latest", "json_schema",
            "https://aistudio.google.com/apikey", new JsonObject { ["reasoning_effort"] = "low" }),
        // Groq. Free tier, no card, very fast. gpt-oss is a reasoning model: keep reasoning low so it does not use up max_tokens.
        ["Groq"] = new("Groq", "https://api.groq.com/openai/v1/", "openai/gpt-oss-120b", "json_object", "https://console.groq.com/keys",
            new JsonObject { ["reasoning_effort"] = "low" }),
        // OpenRouter. Models ending in ":free" cost nothing (small daily request cap).
        ["OpenRouter"] = new("OpenRouter", "https://openrouter.ai/api/v1/", "meta-llama/llama-3.3-70b-instruct:free", "json_object", "https://openrouter.ai/keys"),
        // Mistral La Plateforme. Free "Experiment" plan (phone verification).
        ["Mistral"] = new("Mistral", "https://api.mistral.ai/v1/", "mistral-small-latest", "json_object", "https://console.mistral.ai/api-keys"),
        // GitHub Models. Free with a GitHub account; the key is a personal access token with the "models" permission.
        ["GitHub"] = new("GitHub", "https://models.github.ai/inference/", "openai/gpt-4.1-mini", "json_schema", "https://github.com/settings/personal-access-tokens"),
        // Claude via the Messages API (paid, no free tier).
        ["Anthropic"] = new("Anthropic", "https://api.anthropic.com/v1/", "claude-opus-5-5", "json_schema", "https://platform.claude.com/settings/keys"),
        ["Claude"] = new("Anthropic", "https://api.anthropic.com/v1/", "claude-opus-5-5", "json_schema", "https://platform.claude.com/settings/keys"),
    };
}

/// <summary>Builds the configured client: one provider, or a primary with a fallback.</summary>
public static class LlmClientFactory
{
    public static ILlmClient Create(LlmOptions options, Func<HttpClient> http)
    {
        var primary = CreateOne(options, options, http);
        if (string.IsNullOrWhiteSpace(options.Fallback.Provider) || options.Fallback.Provider.Equals("None", StringComparison.OrdinalIgnoreCase))
            return primary;
        var secondary = CreateOne(options.Fallback, options, http);
        if (!secondary.IsConfigured) return primary;
        return primary.IsConfigured ? new FallbackLlmClient(primary, secondary) : secondary;
    }

    private static ILlmClient CreateOne(LlmEndpointOptions ep, LlmOptions shared, Func<HttpClient> http)
    {
        var name = (ep.Provider ?? "").Trim();
        if (name.Length == 0 || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return new NullLlmClient();

        LlmPreset preset;
        if (name.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ep.BaseUrl) || string.IsNullOrWhiteSpace(ep.Model))
                return new NullLlmClient("Provider 'Custom' benodig AiHelper:Llm:BaseUrl en AiHelper:Llm:Model.");
            preset = new LlmPreset("Custom", ep.BaseUrl, ep.Model, "json_object", "");
        }
        else if (!LlmPresets.All.TryGetValue(name, out preset!))
        {
            return new NullLlmClient($"Onbekende taalmodel-verskaffer '{name}'. Kies Gemini, Groq, OpenRouter, Mistral, GitHub, Anthropic, Custom of None.");
        }

        if (string.IsNullOrWhiteSpace(ep.ApiKey))
            return new NullLlmClient($"Geen API-sleutel vir {preset.Name} is opgestel nie (AiHelper:Llm:ApiKey). Kry een by {preset.KeyUrl}.");

        var resolved = new LlmEndpointOptions
        {
            Provider = preset.Name,
            ApiKey = ep.ApiKey.Trim(),
            Model = string.IsNullOrWhiteSpace(ep.Model) ? preset.DefaultModel : ep.Model.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(ep.BaseUrl) ? preset.BaseUrl : ep.BaseUrl.Trim()
        };
        return preset.Name == "Anthropic"
            ? new AnthropicLlmClient(http, resolved, shared)
            : new OpenAiCompatibleLlmClient(http, resolved, preset, shared);
    }
}

/// <summary>Tries the primary provider, and on any failure (rate limit, outage, bad answer) the fallback. Free tiers make this worth having.</summary>
public sealed class FallbackLlmClient : ILlmClient
{
    private readonly ILlmClient _primary;
    private readonly ILlmClient _fallback;

    public FallbackLlmClient(ILlmClient primary, ILlmClient fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public string Provider => $"{_primary.Provider} (rugsteun: {_fallback.Provider})";
    public string Model => _primary.Model;
    public bool IsConfigured => true;

    public async Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct)
    {
        try
        {
            return await _primary.GenerateJsonAsync(systemPrompt, userPrompt, jsonSchema, ct);
        }
        catch (LlmException first)
        {
            try
            {
                return await _fallback.GenerateJsonAsync(systemPrompt, userPrompt, jsonSchema, ct);
            }
            catch (LlmException second)
            {
                throw new LlmException($"{_primary.Provider}: {first.Message} / {_fallback.Provider}: {second.Message}", second);
            }
        }
    }
}

/// <summary>Shared HTTP plumbing: per-call timeout, readable (Afrikaans) errors, JSON extraction.</summary>
public abstract class HostedLlmClient : ILlmClient
{
    private readonly Func<HttpClient> _http;
    protected readonly LlmEndpointOptions Endpoint;
    protected readonly LlmOptions Shared;

    protected HostedLlmClient(Func<HttpClient> http, LlmEndpointOptions endpoint, LlmOptions shared)
    {
        _http = http;
        Endpoint = endpoint;
        Shared = shared;
    }

    public string Provider => Endpoint.Provider;
    public string Model => Endpoint.Model;
    public bool IsConfigured => true;

    public abstract Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct);

    protected string Url(string path) => Endpoint.BaseUrl.TrimEnd('/') + "/" + path;

    /// <summary>Sends the request; returns (status, body). Converts timeouts and network failures into LlmException.</summary>
    protected async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Shared.TimeoutSeconds));
        try
        {
            using var res = await _http().SendAsync(req, cts.Token);
            return (res.StatusCode, await res.Content.ReadAsStringAsync(cts.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmException($"{Provider} het nie binne {Shared.TimeoutSeconds}s geantwoord nie.");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Kon nie by die {Provider} API uitkom nie.", ex);
        }
    }

    protected LlmException HttpError(HttpStatusCode status, string body) => (int)status switch
    {
        401 or 403 => new LlmException($"{Provider} het die API-sleutel geweier ({(int)status}). Kontroleer AiHelper:Llm:ApiKey."),
        429 => new LlmException($"{Provider} se tempolimiet of gratis kwota is bereik (429). Probeer later weer."),
        404 => new LlmException($"{Provider} ken nie die model '{Model}' nie (404). Stel AiHelper:Llm:Model."),
        _ => new LlmException($"{Provider} het met {(int)status} geantwoord: {Truncate(body, 200)}")
    };

    /// <summary>Pulls the JSON object out of a reply: strips ```json fences and any text before the first '{' or after the last '}'.</summary>
    public static string ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new LlmException("Die taalmodel het 'n leë antwoord teruggegee.");
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new LlmException("Die taalmodel se antwoord bevat geen JSON-objek nie.");
        var json = text[start..(end + 1)];
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new LlmException("Die taalmodel se antwoord was nie geldige JSON nie.", ex); }
        return json;
    }

    protected static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>
/// Any OpenAI-compatible chat-completions API: Gemini, Groq, OpenRouter, Mistral, GitHub Models, or a custom base URL. Asks for
/// schema-constrained JSON where the provider supports it and falls back to plain JSON mode (schema in the prompt) when the model
/// rejects that with a 400.
/// </summary>
public sealed class OpenAiCompatibleLlmClient : HostedLlmClient
{
    private readonly LlmPreset _preset;

    public OpenAiCompatibleLlmClient(Func<HttpClient> http, LlmEndpointOptions endpoint, LlmPreset preset, LlmOptions shared)
        : base(http, endpoint, shared) => _preset = preset;

    public override async Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (status, body) = await PostAsync(systemPrompt, userPrompt, jsonSchema, _preset.JsonMode, ct);
        if ((int)status is 500 or 502 or 503 or 504)
        {
            // Overloaded / temporary error (Gemini answers 503 "high demand" at peak times): one retry before giving up.
            await Task.Delay(Shared.RetryDelayMs, ct);
            (status, body) = await PostAsync(systemPrompt, userPrompt, jsonSchema, _preset.JsonMode, ct);
        }
        if (status == HttpStatusCode.BadRequest && _preset.JsonMode == "json_schema")
            (status, body) = await PostAsync(systemPrompt, userPrompt, jsonSchema, "json_object", ct);
        if ((int)status is < 200 or > 299) throw HttpError(status, body);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var choice = doc.RootElement.GetProperty("choices")[0];
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.GetString() == "length")
                throw new LlmException($"{Provider} se antwoord is afgesny (AiHelper:Llm:MaxTokens te laag).");
            var content = choice.GetProperty("message").GetProperty("content").GetString();
            return new LlmResult(ExtractJsonObject(content), Provider, Model, sw.ElapsedMilliseconds);
        }
        catch (LlmException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new LlmException($"{Provider} se antwoord kon nie gelees word nie.", ex);
        }
    }

    private Task<(HttpStatusCode, string)> PostAsync(string systemPrompt, string userPrompt, string jsonSchema, string mode, CancellationToken ct)
    {
        var system = mode == "json_schema"
            ? systemPrompt
            : systemPrompt + "\n\nReply with ONE JSON object (no markdown, no text around it) that matches this JSON schema:\n" + jsonSchema;
        var body = new JsonObject
        {
            ["model"] = Model,
            ["temperature"] = Shared.Temperature,
            ["max_tokens"] = Shared.MaxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            },
            ["response_format"] = mode == "json_schema"
                ? new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "advice", ["schema"] = JsonNode.Parse(jsonSchema) }
                }
                : new JsonObject { ["type"] = "json_object" }
        };
        if (_preset.ExtraBody != null)
            foreach (var (k, v) in _preset.ExtraBody) body[k] = v?.DeepClone();

        var req = new HttpRequestMessage(HttpMethod.Post, Url("chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Endpoint.ApiKey);
        if (_preset.Name == "OpenRouter") req.Headers.TryAddWithoutValidation("X-Title", "MikPunt AI-Helper");
        return SendAsync(req, ct);
    }
}

/// <summary>
/// Claude via the Messages API with structured outputs (output_config.format = the advice schema, made strict), so the reply is
/// schema-valid JSON in a text block. Current models reject temperature and forced tool choice, so neither is sent. For models
/// that support it the server-side refusal fallback is enabled, so a safety decline is retried on another model in the same call.
/// </summary>
public sealed class AnthropicLlmClient : HostedLlmClient
{
    private static readonly HashSet<string> FallbackModels = new(StringComparer.OrdinalIgnoreCase)
        { "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5", "claude-fable-5-1" };

    public AnthropicLlmClient(Func<HttpClient> http, LlmEndpointOptions endpoint, LlmOptions shared) : base(http, endpoint, shared) { }

    public override async Task<LlmResult> GenerateJsonAsync(string systemPrompt, string userPrompt, string jsonSchema, CancellationToken ct)
    {
        var outputConfig = new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = StrictSchema(jsonSchema) }
        };
        if (!string.IsNullOrWhiteSpace(Shared.AnthropicEffort)) outputConfig["effort"] = Shared.AnthropicEffort;

        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = Shared.AnthropicMaxTokens,
            ["system"] = systemPrompt,
            ["output_config"] = outputConfig,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = userPrompt } }
        };
        bool fallbacks = FallbackModels.Contains(Model);
        if (fallbacks) body["fallbacks"] = "default";

        var req = new HttpRequestMessage(HttpMethod.Post, Url("messages"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", Endpoint.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        if (!string.IsNullOrWhiteSpace(Shared.AnthropicWorkspaceId))
            req.Headers.Add("anthropic-workspace-id", Shared.AnthropicWorkspaceId.Trim());
        if (fallbacks) req.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01");

        var sw = Stopwatch.StartNew();
        var (status, text) = await SendAsync(req, ct);
        if ((int)status is < 200 or > 299) throw HttpError(status, text);

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var stop = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
            if (stop == "refusal") throw new LlmException("Claude het geweier om hierdie versoek te beantwoord.");
            if (stop == "max_tokens") throw new LlmException("Claude se antwoord is afgesny (AiHelper:Llm:AnthropicMaxTokens te laag).");
            var answeredBy = root.TryGetProperty("model", out var m) ? m.GetString() ?? Model : Model;
            foreach (var block in root.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                    return new LlmResult(ExtractJsonObject(block.GetProperty("text").GetString()), Provider, answeredBy, sw.ElapsedMilliseconds);
            }
            throw new LlmException("Claude het geen teksantwoord teruggegee nie.");
        }
        catch (LlmException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmException("Claude se antwoord kon nie gelees word nie.", ex);
        }
    }

    /// <summary>
    /// Structured outputs need additionalProperties:false on every object and do not support numeric or array-size constraints
    /// (minimum/maximum/minItems/maxItems), so those are removed here; ParseNarrative enforces the limits after the fact.
    /// </summary>
    public static JsonNode StrictSchema(string jsonSchema)
    {
        var node = JsonNode.Parse(jsonSchema)!;
        Strip(node);
        return node;

        static void Strip(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var k in new[] { "minimum", "maximum", "minItems", "maxItems", "minLength", "maxLength" }) o.Remove(k);
                    if (o["type"] is JsonValue tv && tv.TryGetValue<string>(out var type) && type == "object")
                        o["additionalProperties"] = false;
                    foreach (var (_, child) in o.ToList()) Strip(child);
                    break;
                case JsonArray a:
                    foreach (var child in a) Strip(child);
                    break;
            }
        }
    }
}
