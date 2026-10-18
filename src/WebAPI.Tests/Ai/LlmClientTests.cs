using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

/// <summary>The hosted-API LLM clients, against a fake HTTP handler: request shape per provider, JSON extraction, retries and fallback.</summary>
public class LlmClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public FakeHandler Reply(HttpStatusCode status, string body)
        {
            _replies.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.InternalServerError, "no reply queued");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        public JsonObject BodyAt(int i) => JsonNode.Parse(Requests[i].Body)!.AsObject();
    }

    private static string ChatReply(string content, string finish = "stop") =>
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = finish,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content }
            })
        }.ToJsonString();

    private static ILlmClient Create(FakeHandler h, LlmOptions o) => LlmClientFactory.Create(o, () => new HttpClient(h));

    private static Task<LlmResult> Ask(ILlmClient c) => c.GenerateJsonAsync("system", "user", AiNarrativeSchema.Json, CancellationToken.None);

    [Fact]
    public void NoApiKey_MeansNumbersOnly_WithAHelpfulReason()
    {
        var c = LlmClientFactory.Create(new LlmOptions { Provider = "Gemini" }, () => new HttpClient());
        Assert.False(c.IsConfigured);
        var ex = Assert.ThrowsAsync<LlmException>(() => Ask(c)).Result;
        Assert.Contains("aistudio.google.com", ex.Message);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Ollama")]          // no longer supported: we do not host a model
    [InlineData("")]
    public void NoneUnknownOrBlankProvider_IsNotConfigured(string provider)
    {
        Assert.False(LlmClientFactory.Create(new LlmOptions { Provider = provider, ApiKey = "k" }, () => new HttpClient()).IsConfigured);
    }

    [Fact]
    public async Task Gemini_UsesTheOpenAiCompatibleEndpoint_WithSchemaOutput_AndStripsMarkdownFences()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ChatReply("```json\n{\"headline\":\"Goed\"}\n```"));
        var c = Create(h, new LlmOptions { Provider = "Gemini", ApiKey = "g-key" });

        var res = await Ask(c);

        Assert.Equal("{\"headline\":\"Goed\"}", res.Json);
        Assert.Equal("Gemini", res.Provider);
        var (req, _) = h.Requests.Single();
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer g-key", req.Headers.GetValues("Authorization").Single());
        var body = h.BodyAt(0);
        Assert.Equal("gemini-flash-latest", (string?)body["model"]);
        Assert.Equal("json_schema", (string?)body["response_format"]!["type"]);
        Assert.Equal("low", (string?)body["reasoning_effort"]);
    }

    [Fact]
    public async Task ModelThatRejectsSchemaOutput_IsRetriedInJsonMode_WithTheSchemaInThePrompt()
    {
        var h = new FakeHandler()
            .Reply(HttpStatusCode.BadRequest, "{\"error\":\"response_format json_schema not supported\"}")
            .Reply(HttpStatusCode.OK, ChatReply("{\"headline\":\"x\"}"));
        var c = Create(h, new LlmOptions { Provider = "Gemini", ApiKey = "k", Model = "some-older-model" });

        await Ask(c);

        Assert.Equal(2, h.Requests.Count);
        var retry = h.BodyAt(1);
        Assert.Equal("json_object", (string?)retry["response_format"]!["type"]);
        Assert.Contains("titleSuggestions", (string?)retry["messages"]![0]!["content"]);
        Assert.Equal("some-older-model", (string?)retry["model"]);
    }

    [Fact]
    public async Task Groq_AsksForAJsonObject_AndPutsTheSchemaInTheSystemPrompt()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ChatReply("Hier is dit: {\"headline\":\"x\"} - klaar"));
        var res = await Ask(Create(h, new LlmOptions { Provider = "Groq", ApiKey = "k" }));

        Assert.Equal("{\"headline\":\"x\"}", res.Json);
        Assert.StartsWith("https://api.groq.com/openai/v1/chat/completions", h.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal("json_object", (string?)h.BodyAt(0)["response_format"]!["type"]);
        Assert.Contains("JSON schema", (string?)h.BodyAt(0)["messages"]![0]!["content"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.Unauthorized, "API-sleutel")]
    [InlineData(HttpStatusCode.NotFound, "model")]
    public async Task HttpErrors_BecomeReadableLlmExceptions(HttpStatusCode status, string expected)
    {
        var h = new FakeHandler().Reply(status, "{}");
        var ex = await Assert.ThrowsAsync<LlmException>(() => Ask(Create(h, new LlmOptions { Provider = "Groq", ApiKey = "k" })));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task TruncatedAnswer_IsAnError_NotHalfAnAdvice()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ChatReply("{\"headline\":\"x\"}", finish: "length"));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Ask(Create(h, new LlmOptions { Provider = "Groq", ApiKey = "k" })));
        Assert.Contains("afgesny", ex.Message);
    }

    [Fact]
    public async Task RateLimitedPrimary_FallsBackToTheSecondProvider()
    {
        var h = new FakeHandler()
            .Reply(HttpStatusCode.TooManyRequests, "{}")
            .Reply(HttpStatusCode.OK, ChatReply("{\"headline\":\"van groq\"}"));
        var c = Create(h, new LlmOptions
        {
            Provider = "Gemini", ApiKey = "g",
            Fallback = new LlmEndpointOptions { Provider = "Groq", ApiKey = "q" }
        });

        var res = await Ask(c);

        Assert.Equal("Groq", res.Provider);
        Assert.Contains("api.groq.com", h.Requests[1].Request.RequestUri!.ToString());
        Assert.Contains("rugsteun", c.Provider);
    }

    [Fact]
    public async Task BothProvidersFailing_ReportsBoth()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.TooManyRequests, "{}").Reply(HttpStatusCode.ServiceUnavailable, "down");
        var c = Create(h, new LlmOptions { Provider = "Gemini", ApiKey = "g", RetryDelayMs = 0, Fallback = new LlmEndpointOptions { Provider = "Groq", ApiKey = "q" } });
        var ex = await Assert.ThrowsAsync<LlmException>(() => Ask(c));
        Assert.Contains("Gemini", ex.Message);
        Assert.Contains("Groq", ex.Message);
    }

    [Fact]
    public async Task TemporaryOverload_IsRetriedOnce_OnTheSameProvider()
    {
        var h = new FakeHandler()
            .Reply(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"high demand\"}}")
            .Reply(HttpStatusCode.OK, ChatReply("{\"headline\":\"tweede keer\"}"));
        var res = await Ask(Create(h, new LlmOptions { Provider = "Gemini", ApiKey = "g", RetryDelayMs = 0 }));
        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("tweede keer", res.Json);
    }

    [Fact]
    public async Task Groq_DefaultsToGptOss_WithLowReasoning()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ChatReply("{\"headline\":\"x\"}"));
        await Ask(Create(h, new LlmOptions { Provider = "Groq", ApiKey = "k" }));
        Assert.Equal("openai/gpt-oss-120b", (string?)h.BodyAt(0)["model"]);
        Assert.Equal("low", (string?)h.BodyAt(0)["reasoning_effort"]);
    }

    [Fact]
    public void FallbackWithoutAKey_IsIgnored_PrimaryStillUsed()
    {
        var c = LlmClientFactory.Create(new LlmOptions { Provider = "Gemini", ApiKey = "g", Fallback = new LlmEndpointOptions { Provider = "Groq" } }, () => new HttpClient());
        Assert.Equal("Gemini", c.Provider);
    }

    [Fact]
    public void CustomProvider_NeedsBaseUrlAndModel()
    {
        Assert.False(LlmClientFactory.Create(new LlmOptions { Provider = "Custom", ApiKey = "k" }, () => new HttpClient()).IsConfigured);
        Assert.True(LlmClientFactory.Create(new LlmOptions { Provider = "Custom", ApiKey = "k", BaseUrl = "https://x.example/v1", Model = "m" }, () => new HttpClient()).IsConfigured);
    }

    // ------------------------------------------------------------------ Anthropic

    private static string ClaudeReply(string json, string stop = "end_turn") => new JsonObject
    {
        ["model"] = "claude-opus-5-5",
        ["stop_reason"] = stop,
        ["content"] = new JsonArray(
            new JsonObject { ["type"] = "thinking", ["thinking"] = "" },
            new JsonObject { ["type"] = "text", ["text"] = json })
    }.ToJsonString();

    [Fact]
    public async Task Anthropic_UsesStructuredOutputs_WithoutTemperatureOrForcedToolChoice()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ClaudeReply("{\"headline\":\"Goed\"}"));
        var res = await Ask(Create(h, new LlmOptions { Provider = "Anthropic", ApiKey = "sk" }));

        Assert.Equal("{\"headline\":\"Goed\"}", res.Json);
        var (req, _) = h.Requests.Single();
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("server-side-fallback-2026-07-01", req.Headers.GetValues("anthropic-beta").Single());

        var body = h.BodyAt(0);
        Assert.Equal("claude-opus-5-5", (string?)body["model"]);
        Assert.Null(body["temperature"]);
        Assert.Null(body["tool_choice"]);
        Assert.Null(body["tools"]);
        Assert.Equal("default", (string?)body["fallbacks"]);
        Assert.Equal("low", (string?)body["output_config"]!["effort"]);
        Assert.Equal("json_schema", (string?)body["output_config"]!["format"]!["type"]);
    }

    [Fact]
    public void Anthropic_StrictSchema_ClosesEveryObject_AndDropsUnsupportedConstraints()
    {
        var schema = AnthropicLlmClient.StrictSchema(AiNarrativeSchema.Json).ToJsonString();
        Assert.DoesNotContain("minItems", schema);
        Assert.DoesNotContain("maxItems", schema);
        Assert.DoesNotContain("\"minimum\"", schema);
        var root = JsonNode.Parse(schema)!;
        Assert.False((bool)root["additionalProperties"]!);
        Assert.False((bool)root["properties"]!["tips"]!["items"]!["additionalProperties"]!);
        Assert.False((bool)root["properties"]!["upsells"]!["items"]!["additionalProperties"]!);
    }

    [Fact]
    public async Task Anthropic_OlderModel_GetsNoFallbackParameter()
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ClaudeReply("{\"headline\":\"x\"}"));
        await Ask(Create(h, new LlmOptions { Provider = "Anthropic", ApiKey = "sk", Model = "claude-haiku-5-5" }));
        Assert.Null(h.BodyAt(0)["fallbacks"]);
        Assert.False(h.Requests[0].Request.Headers.Contains("anthropic-beta"));
    }

    [Theory]
    [InlineData("refusal", "geweier")]
    [InlineData("max_tokens", "afgesny")]
    public async Task Anthropic_StopReasons_AreErrors(string stop, string expected)
    {
        var h = new FakeHandler().Reply(HttpStatusCode.OK, ClaudeReply("{\"headline\":\"x\"}", stop));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Ask(Create(h, new LlmOptions { Provider = "Anthropic", ApiKey = "sk" })));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void NonJsonAnswer_IsRejected()
    {
        Assert.Throws<LlmException>(() => HostedLlmClient.ExtractJsonObject("Jammer, ek kan nie help nie."));
        Assert.Throws<LlmException>(() => HostedLlmClient.ExtractJsonObject("{ nie json }"));
        Assert.Equal("{\"a\":1}", HostedLlmClient.ExtractJsonObject("```json\n{\"a\":1}\n```"));
    }
}
