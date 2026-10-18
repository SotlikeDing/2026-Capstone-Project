using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Services.Ai;

public interface ITeamMatcher
{
    Task<AiTeamResponse> FindAsync(AiTeamRequest request, int userId, CancellationToken ct);
}

/// <summary>
/// Smart Teaming: ranks possible team partners. A PJ gets PH / VG / Boer candidates; a PH, VG or Boer gets outfitters (PJ).
///   1. FEATURES - four 0-1 components per candidate from real platform data: proximity (shared / neighbouring province),
///      credentials (rating, shrunk towards 3.5 when there are few ratings, plus experience), rate fit (PJ's budget) or work
///      volume, and history (hunts worked together, completed hunts).
///   2. SCORE    - the plan's prior weights (35/25/20/20), blended with a logistic-regression model trained on the platform's own
///      invite outcomes (accepted / declined, existing teams). The learned share grows with the data: n / (n + 50), capped at 80%.
///   3. WORDS    - an LLM writes a short "why this partner" per candidate. It sees anonymised facts only (no names, contact
///      details or bios), because the LLM is an external API.
/// AP users are excluded: advertisers do not take part in field teams.
/// </summary>
public sealed class TeamMatchService : ITeamMatcher
{
    public const string ApExclusionMessage =
        "Adverteerders (AP) bemark kommersiële produkte/dienste op die markplein en neem nie deel aan jagspan-teaming in die veld nie. " +
        "Teaming is beskikbaar vir Uitrusters (PJ), Professionele Jagters (PH), Videograwe (VG) en Boere.";

    public static readonly IReadOnlyDictionary<string, double> PriorWeights = new Dictionary<string, double>
    {
        ["proximity"] = 0.35, ["credentials"] = 0.25, ["rate"] = 0.20, ["history"] = 0.20
    };
    private static readonly string[] Keys = { "proximity", "credentials", "rate", "history" };

    private readonly ITeamMatchStore _store;
    private readonly ILlmClient _llm;
    private readonly ILogger<TeamMatchService> _logger;

    public TeamMatchService(ITeamMatchStore store, ILlmClient llm, ILogger<TeamMatchService> logger)
    {
        _store = store;
        _llm = llm;
        _logger = logger;
    }

    public async Task<AiTeamResponse> FindAsync(AiTeamRequest request, int userId, CancellationToken ct)
    {
        var role = PackageAdvisorService.NormalizeRole(request.RoleCode) ?? throw new AiValidationException("Ongeldige rol vir die AI-helper.");
        var res = new AiTeamResponse { RoleCode = role };
        res.Model.PriorWeights = PriorWeights.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (role == "AP")
        {
            res.Excluded = true;
            res.ExclusionMessage = ApExclusionMessage;
            return res;
        }
        if (request.MaxDailyRate is < 0 or > 10_000_000) throw new AiValidationException("Begroting moet tussen 0 en 10 000 000 wees.");
        string? askedProvince = null;
        if (!string.IsNullOrWhiteSpace(request.Province))
            askedProvince = SpeciesCatalog.TryNormalizeProvince(request.Province) ?? throw new AiValidationException("Onbekende provinsie.");

        var graph = await _store.LoadGraphAsync();
        InheritProvincesFromTeams(graph);

        var viewer = graph.ByUserRoleId.Values.FirstOrDefault(p => p.UserId == userId && p.RoleCode == role);
        var viewerProvinces = askedProvince != null ? new HashSet<string> { askedProvince } : viewer?.Provinces ?? new HashSet<string>();
        res.ViewerProvinces = viewerProvinces.OrderBy(p => p).ToList();

        var wanted = role == "PJ"
            ? (request.WantRoles.Count == 0 ? new[] { "PH", "VG", "FARMER" } : request.WantRoles.Select(PackageAdvisorService.NormalizeRole)
                .Where(r => r is "PH" or "VG" or "FARMER").Select(r => r!).Distinct().ToArray())
            : new[] { "PJ" };

        var learned = LogisticModel.TrainFrom(graph);
        double share = learned == null ? 0 : Math.Min(0.8, learned.Samples / (learned.Samples + 50.0));
        res.Model.LabelledPairs = learned?.Samples ?? CountLabels(graph);
        if (learned != null)
        {
            res.Model.Source = "prior+learned";
            res.Model.LearnedShare = Math.Round(share, 2);
            res.Model.LearnedCoefficients = Keys.Select((k, i) => (k, learned.Weights[i])).ToDictionary(x => x.k, x => Math.Round(x.Item2, 3));
        }

        int take = Math.Clamp(request.Take, 1, 20);
        var matches = new List<AiTeamMatch>();
        foreach (var cand in graph.ByUserRoleId.Values.Where(c => c.Visible && c.UserId != userId && wanted.Contains(c.RoleCode)))
        {
            var pair = role == "PJ" ? (viewer?.UserRoleId ?? 0, cand.UserRoleId) : (cand.UserRoleId, viewer?.UserRoleId ?? 0);
            var relationship = Relationship(graph, pair);
            if (relationship is "team" or "invite_accepted") continue;     // already partners - not a suggestion
            int joint = graph.JointHunts.GetValueOrDefault(pair);

            var comps = role == "PJ"
                ? ForPj(viewerProvinces, cand, request.MaxDailyRate, joint)
                : ForMember(viewerProvinces, cand, joint);
            double prior = comps.Sum(c => c.Value * c.Weight);
            double score = prior;
            if (learned != null)
            {
                var pjSide = role == "PJ" ? viewerProvinces : cand.Provinces;
                var member = role == "PJ" ? cand : viewer;
                double p = member == null ? prior : learned.Predict(PairFeatures(pjSide, member, joint));
                score = (1 - share) * prior + share * p;
            }

            matches.Add(new AiTeamMatch
            {
                UserId = cand.UserId, UserRoleId = cand.UserRoleId, Name = cand.Name,
                BusinessName = string.IsNullOrWhiteSpace(cand.BusinessName) ? null : cand.BusinessName,
                RoleCode = cand.RoleCode, Provinces = cand.Provinces.OrderBy(p => p).ToList(),
                Score = (int)Math.Round(100 * Math.Clamp(score, 0, 1)), Components = comps,
                Facts = Facts(role, cand, viewerProvinces, request.MaxDailyRate, joint),
                RatingAverage = cand.RatingAverage, RatingCount = cand.RatingCount, YearsExperience = cand.YearsExperience,
                DailyRate = cand.DailyRate, CompletedHunts = cand.CompletedHunts, JointHunts = joint,
                Relationship = relationship,
                CanInvite = role == "PJ" && viewer != null && relationship != "invite_pending"
            });
        }
        res.Matches = matches.OrderByDescending(m => m.Score).ThenByDescending(m => m.RatingCount).ThenBy(m => m.Name).Take(take).ToList();

        await AddNarrativeAsync(res, role, ct);
        return res;
    }

    // ================================================================== features

    private static List<AiTeamComponent> ForPj(HashSet<string> viewerProv, TeamProfile c, decimal? budget, int joint) => new()
    {
        Comp("proximity", "Nabyheid", Proximity(viewerProv, c.Provinces)),
        Comp("credentials", "Geloofsbriewe & gradering", Credentials(c)),
        Comp("rate", "Tarief teen u begroting", RateFit(c.DailyRate, budget)),
        Comp("history", "Rekord (saam & voltooide jagte)", 0.6 * Saturate(joint, 3) + 0.4 * Saturate(c.CompletedHunts, 10))
    };

    private static List<AiTeamComponent> ForMember(HashSet<string> viewerProv, TeamProfile pj, int joint) => new()
    {
        Comp("proximity", "Nabyheid", Proximity(viewerProv, pj.Provinces)),
        Comp("credentials", "Geloofsbriewe & gradering", Credentials(pj)),
        Comp("rate", "Werkvolume (voltooide jagte)", Saturate(pj.CompletedHunts, 10)),
        Comp("history", "Al saam gewerk", Saturate(joint, 3))
    };

    /// <summary>The feature vector the learned model uses, always from the PJ's side of the pair so every labelled pair is comparable.</summary>
    public static double[] PairFeatures(HashSet<string> pjProvinces, TeamProfile member, int joint) => new[]
    {
        Proximity(pjProvinces, member.Provinces), Credentials(member), RateFit(member.DailyRate, null),
        0.6 * Saturate(joint, 3) + 0.4 * Saturate(member.CompletedHunts, 10)
    };

    private static AiTeamComponent Comp(string key, string label, double v) =>
        new() { Key = key, Label = label, Value = Math.Round(v, 3), Weight = PriorWeights[key] };

    public static double Saturate(double n, double full) => Math.Clamp(n / full, 0, 1);

    /// <summary>1 = same province, 0.6 = neighbouring, 0.15 = far apart, 0.5 = unknown on either side (no penalty for missing data).</summary>
    public static double Proximity(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.5;
        if (a.Any(b.Contains)) return 1.0;
        if (a.Any(x => Neighbours.TryGetValue(x, out var n) && b.Any(n.Contains))) return 0.6;
        return 0.15;
    }

    /// <summary>Rating shrunk towards 3.5 stars with the weight of 5 virtual ratings (so one 5-star review is not "perfect"), plus experience (20 years = full).</summary>
    public static double Credentials(TeamProfile p)
    {
        double n = p.RatingCount;
        double avg = p.RatingAverage ?? 3.5;
        double shrunk = (avg * n + 3.5 * 5) / (n + 5);
        return 0.6 * Math.Clamp((shrunk - 1) / 4, 0, 1) + 0.4 * Saturate(p.YearsExperience, 20);
    }

    /// <summary>1 when the rate fits the budget, falling linearly to 0 at twice the budget; 0.5 when either is unknown.</summary>
    public static double RateFit(decimal? rate, decimal? budget)
    {
        if (rate is not > 0 || budget is not > 0) return 0.5;
        if (rate <= budget) return 1.0;
        return Math.Clamp(1 - (double)((rate.Value - budget.Value) / budget.Value), 0, 1);
    }

    private static readonly Dictionary<string, string[]> Neighbours = new()
    {
        ["limpopo"] = new[] { "north_west", "gauteng", "mpumalanga" },
        ["gauteng"] = new[] { "limpopo", "north_west", "free_state", "mpumalanga" },
        ["mpumalanga"] = new[] { "limpopo", "gauteng", "free_state", "kwazulu_natal" },
        ["north_west"] = new[] { "limpopo", "gauteng", "free_state", "northern_cape" },
        ["free_state"] = new[] { "north_west", "gauteng", "mpumalanga", "kwazulu_natal", "eastern_cape", "northern_cape" },
        ["northern_cape"] = new[] { "north_west", "free_state", "eastern_cape", "western_cape" },
        ["eastern_cape"] = new[] { "northern_cape", "free_state", "kwazulu_natal", "western_cape" },
        ["western_cape"] = new[] { "northern_cape", "eastern_cape" },
        ["kwazulu_natal"] = new[] { "mpumalanga", "free_state", "eastern_cape" },
    };

    public static readonly IReadOnlyDictionary<string, string> ProvinceNames = new Dictionary<string, string>
    {
        ["limpopo"] = "Limpopo", ["gauteng"] = "Gauteng", ["mpumalanga"] = "Mpumalanga", ["north_west"] = "Noordwes",
        ["free_state"] = "Vrystaat", ["northern_cape"] = "Noord-Kaap", ["eastern_cape"] = "Oos-Kaap", ["western_cape"] = "Wes-Kaap",
        ["kwazulu_natal"] = "KwaZulu-Natal"
    };

    /// <summary>A PH / VG / Boer without listings of their own inherits the provinces of the PJ teams they belong to (a weaker but real signal).</summary>
    private static void InheritProvincesFromTeams(TeamGraph g)
    {
        foreach (var (pjUr, memberUr) in g.TeamRows)
        {
            if (g.ByUserRoleId.TryGetValue(memberUr, out var m) && m.Provinces.Count == 0 && g.ByUserRoleId.TryGetValue(pjUr, out var pj))
                foreach (var p in pj.Provinces) m.Provinces.Add(p);
        }
    }

    private static string Relationship(TeamGraph g, (int, int) pair)
    {
        if (g.TeamRows.Contains(pair)) return "team";
        if (!g.Invites.TryGetValue(pair, out var s)) return "none";
        return s.Trim().ToLowerInvariant() switch
        {
            "accepted" or "aanvaar" => "invite_accepted",
            "declined" or "rejected" or "afgekeur" => "invite_declined",
            "pending" => "invite_pending",
            _ => "none"
        };
    }

    private static string Money(decimal v) => "R" + v.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static List<string> Facts(string viewerRole, TeamProfile c, HashSet<string> viewerProv, decimal? budget, int joint)
    {
        var f = new List<string>();
        if (c.Provinces.Count > 0)
        {
            var names = string.Join(", ", c.Provinces.OrderBy(p => p).Select(p => ProvinceNames.GetValueOrDefault(p, p)));
            f.Add(viewerProv.Overlaps(c.Provinces) ? $"Werk in {names} - dieselfde gebied as u." : $"Werk in {names}.");
        }
        else f.Add("Werksgebied nog nie op MikPunt bekend nie.");
        f.Add(c.RatingCount > 0 ? $"Gradering {c.RatingAverage:0.0} uit 5 ({c.RatingCount} gradering{(c.RatingCount == 1 ? "" : "s")})." : "Nog geen graderings nie.");
        if (c.YearsExperience > 0) f.Add($"{c.YearsExperience} jaar ervaring.");
        if (viewerRole == "PJ" && c.DailyRate is > 0)
        {
            string label = c.RoleCode switch { "FARMER" => "Lodge-dagtarief", "VG" => "Video-dagtarief", _ => "Dagtarief" };
            string fit = budget is > 0 ? (c.DailyRate <= budget ? " (binne u begroting)" : " (bo u begroting)") : "";
            f.Add($"{label} {Money(c.DailyRate.Value)}{fit}.");
        }
        if (c.CompletedHunts > 0) f.Add($"{c.CompletedHunts} voltooide jag{(c.CompletedHunts == 1 ? "" : "te")} op MikPunt.");
        if (joint > 0) f.Add($"Al {joint} keer saam gewerk.");
        return f;
    }

    private static int CountLabels(TeamGraph g) =>
        g.TeamRows.Count + g.Invites.Count(kv => kv.Value.Trim().ToLowerInvariant() is "accepted" or "declined" or "rejected");

    // ================================================================== LLM reasons

    private const string TeamSystemPrompt = """
        You are MikPunt's team-building advisor for the South African hunting industry. CONTEXT lists candidate partners (K1, K2, ...)
        for the user, already ranked by a scoring model, with plain facts and component scores (0-1) for each.

        Rules - follow all of them:
        1. Write ALL text in Afrikaans (South African), professional and friendly.
        2. For EVERY candidate write a "reason" of 1-2 sentences: why this candidate is a good fit for the user, based ONLY on its facts and
           component scores. If a fact is weak or unknown (no ratings, unknown area, over budget), say so honestly in a few words.
        3. Never invent facts, names, prices, numbers or credentials. Only use numbers that appear in the facts.
        4. Write in plain language for the user: do NOT quote the component scores, the match score or decimals such as 0.5 - say
           "sterk", "gemiddeld" or "onbekend" instead.
        5. Candidates are anonymous: never guess a gender (no "hy"/"sy") - write "hierdie kandidaat", "die PH", "die uitruster", etc.
        6. "summary": 1-2 sentences on how to approach these candidates (e.g. who to contact first and why).
        7. Reply only with the JSON object that matches the required schema; use the candidate ids exactly as given.
        """;

    private const string TeamSchema = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string" },
            "matches": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": { "id": { "type": "string" }, "reason": { "type": "string" } },
                "required": ["id", "reason"]
              }
            }
          },
          "required": ["summary", "matches"]
        }
        """;

    private static string RoleLabel(string r) => r switch
    {
        "PJ" => "uitruster (PJ)", "PH" => "professionele jagter (PH)", "VG" => "videograaf (VG)", "FARMER" => "boer / grondeienaar", _ => r
    };

    private async Task AddNarrativeAsync(AiTeamResponse res, string role, CancellationToken ct)
    {
        res.Engine.LlmProvider = _llm.Provider;
        res.Engine.LlmModel = _llm.Model;
        if (res.Matches.Count == 0) { res.NarrativeStatus = _llm.IsConfigured ? "ok" : "disabled"; return; }

        var top = res.Matches.Take(8).ToList();
        var ctx = new JsonObject
        {
            ["userRole"] = RoleLabel(role),
            ["userArea"] = res.ViewerProvinces.Count == 0 ? "onbekend" : string.Join(", ", res.ViewerProvinces.Select(p => ProvinceNames.GetValueOrDefault(p, p))),
            ["candidates"] = new JsonArray(top.Select((m, i) => (JsonNode)new JsonObject
            {
                ["id"] = $"K{i + 1}",
                ["role"] = RoleLabel(m.RoleCode),
                ["matchScore"] = m.Score,
                ["components"] = new JsonObject(m.Components.Select(c => KeyValuePair.Create(c.Label, (JsonNode?)JsonValue.Create(c.Value)))),
                ["facts"] = new JsonArray(m.Facts.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray())
            }).ToArray())
        };
        var prompt = "CONTEXT (JSON):\n" + ctx.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                     + "\n\nWrite the reasons now, following all rules.";
        try
        {
            var llm = await _llm.GenerateJsonAsync(TeamSystemPrompt, prompt, TeamSchema, ct);
            using var doc = JsonDocument.Parse(llm.Json);
            var root = doc.RootElement;
            if (root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                res.Summary = PackageAdvisorService.Clean(s.GetString(), 600);
            if (root.TryGetProperty("matches", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                    var idText = id.GetString()!.Trim().TrimStart('K', 'k');
                    if (int.TryParse(idText, out var n) && n >= 1 && n <= top.Count && e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                        top[n - 1].Reason = PackageAdvisorService.Clean(r.GetString(), 500);
                }
            }
            res.NarrativeStatus = "ok";
            res.Engine.LlmProvider = llm.Provider;
            res.Engine.LlmModel = llm.Model;
            res.Engine.LatencyMs = llm.LatencyMs;
        }
        catch (Exception ex) when (ex is LlmException or JsonException)
        {
            res.NarrativeStatus = _llm.IsConfigured ? "unavailable" : "disabled";
            res.NarrativeError = ex.Message;
            _logger.LogWarning("[AI] Team reasons unavailable: {Message}", ex.Message);
        }
    }
}

/// <summary>
/// A small L2-regularised logistic regression over the four pair features, trained on the platform's own outcomes: accepted
/// invites and existing teams are positives, declined invites negatives. Returns null until there is enough evidence
/// (at least 10 labels with 3 of each class) - until then the prior weights carry the score alone.
/// </summary>
public sealed class LogisticModel
{
    public double[] Weights { get; } = new double[4];
    public double Bias { get; private set; }
    public int Samples { get; private set; }

    public double Predict(double[] x)
    {
        double z = Bias;
        for (int i = 0; i < Weights.Length; i++) z += Weights[i] * x[i];
        return 1 / (1 + Math.Exp(-z));
    }

    public static LogisticModel? TrainFrom(TeamGraph g)
    {
        var labels = new Dictionary<(int, int), int>();
        foreach (var (pair, status) in g.Invites)
        {
            var s = status.Trim().ToLowerInvariant();
            if (s is "accepted" or "aanvaar") labels[pair] = 1;
            else if (s is "declined" or "rejected" or "afgekeur") labels[pair] = 0;
        }
        foreach (var pair in g.TeamRows) labels[pair] = 1;

        var data = new List<(double[] X, int Y)>();
        foreach (var ((pjUr, memberUr), y) in labels)
        {
            if (!g.ByUserRoleId.TryGetValue(pjUr, out var pj) || !g.ByUserRoleId.TryGetValue(memberUr, out var m)) continue;
            data.Add((TeamMatchService.PairFeatures(pj.Provinces, m, g.JointHunts.GetValueOrDefault((pjUr, memberUr))), y));
        }
        return Train(data);
    }

    public static LogisticModel? Train(IReadOnlyList<(double[] X, int Y)> data, int epochs = 800, double lr = 0.5, double l2 = 0.01)
    {
        int pos = data.Count(d => d.Y == 1), neg = data.Count - pos;
        if (data.Count < 10 || pos < 3 || neg < 3) return null;

        var m = new LogisticModel { Samples = data.Count };
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            var grad = new double[4];
            double gradB = 0;
            foreach (var (x, y) in data)
            {
                double err = m.Predict(x) - y;
                for (int i = 0; i < 4; i++) grad[i] += err * x[i];
                gradB += err;
            }
            for (int i = 0; i < 4; i++) m.Weights[i] -= lr * (grad[i] / data.Count + l2 * m.Weights[i]);
            m.Bias -= lr * gradB / data.Count;
        }
        return m;
    }
}
