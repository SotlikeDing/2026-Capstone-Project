namespace WebAPI.Services.Ai;

/// <summary>Bound from the "AiHelper" appsettings section. Secrets (Llm:ApiKey, Llm:Fallback:ApiKey) belong in user-secrets / environment variables, never in appsettings.json.</summary>
public class AiHelperOptions
{
    public const string SectionName = "AiHelper";

    /// <summary>Folder (relative to the app's base directory) holding the trained price-model bundle produced by tools/ai/train_price_model.py.</summary>
    public string ModelDirectory { get; set; } = "AiModels/package_price";

    /// <summary>
    /// Optional persistent folder (a Docker volume in deployment, e.g. /data/ai-models) holding a retrained model bundle in its
    /// "current" subfolder. At startup/reload the bundle with the NEWER training timestamp wins between this and the one baked into
    /// the image, so a redeploy can never be beaten by a stale volume and a retrained model survives redeploys. Empty = disabled.
    /// </summary>
    public string ModelOverrideDirectory { get; set; } = "";

    /// <summary>ZAR per 1 USD. The price models work in USD (the currency the international market publishes in); this converts to and from ZAR. Deliberately configuration, not a hard-coded constant.</summary>
    public decimal UsdToZarRate { get; set; } = 18.0m;

    public LlmOptions Llm { get; set; } = new();
    public LearningOptions Learning { get; set; } = new();
}

/// <summary>
/// One hosted language-model API, reached with an API key. Nothing is self-hosted: the numbers come from the in-process price
/// model, the words from this API. <see cref="Provider"/> picks a preset (base URL + default model, see <see cref="LlmPresets"/>);
/// Model and BaseUrl only need setting to override the preset.
/// </summary>
public class LlmEndpointOptions
{
    /// <summary>Gemini, Groq, OpenRouter, Mistral, GitHub, Anthropic, Custom (any OpenAI-compatible API - set BaseUrl and Model) or None (numbers only).</summary>
    public string Provider { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>Empty = the preset's default model.</summary>
    public string Model { get; set; } = "";
    /// <summary>Empty = the preset's base URL. Required for Custom.</summary>
    public string BaseUrl { get; set; } = "";
}

/// <summary>Which language model writes the advice text. The numbers always come from the trained price model, never from the LLM.</summary>
public class LlmOptions : LlmEndpointOptions
{
    public LlmOptions() => Provider = "Gemini";

    /// <summary>Optional second provider, tried when the first one fails (free tiers hit rate limits). Leave Provider empty to disable.</summary>
    public LlmEndpointOptions Fallback { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>Wait before the single retry after a temporary 5xx error from a provider.</summary>
    public int RetryDelayMs { get; set; } = 2000;
    /// <summary>Sampling temperature for OpenAI-compatible providers. Not sent to Anthropic: current Claude models reject it.</summary>
    public double Temperature { get; set; } = 0.3;
    /// <summary>Output-token cap for OpenAI-compatible providers.</summary>
    public int MaxTokens { get; set; } = 4096;
    /// <summary>Anthropic only: output-token cap. Thinking counts against it, so keep it generous.</summary>
    public int AnthropicMaxTokens { get; set; } = 16000;
    /// <summary>Anthropic only: output_config.effort ("low" is plenty for short structured advice and keeps cost down).</summary>
    public string AnthropicEffort { get; set; } = "low";
    /// <summary>Anthropic only: optional workspace ID (required when using a user-scoped API key sk-ant-usr-*).</summary>
    public string AnthropicWorkspaceId { get; set; } = "";
}

/// <summary>Continuous-learning settings: the periodic harvest of real platform listings into aiMarketObservations.</summary>
public class LearningOptions
{
    /// <summary>0 disables the background harvest (an admin can still POST api/aihelper/admin/harvest).</summary>
    public int HarvestIntervalHours { get; set; } = 24;
}
