using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebAPI.Models;
using WebAPI.Services.Ai;

namespace WebAPI.Database;

/// <summary>A logged piece of advice: the exact context the LLM saw and what it answered, so feedback can be judged against both.</summary>
public sealed record AdviceLogEntry(
    int UserId, string RoleCode, string? Kind, string ContextJson, string? NarrativeJson,
    decimal? UserPriceUsd, decimal? P10Usd, decimal? P50Usd, decimal? P90Usd,
    string? ModelVersion, string? LlmProvider, string? LlmModel, string NarrativeStatus, int LatencyMs);

/// <summary>Rows written to aiMarketObservations by one harvest run, per kind.</summary>
public sealed class HarvestResult
{
    public Dictionary<string, int> UpsertedByKind { get; } = new();
    public List<string> Skipped { get; } = new();
    public int Total => UpsertedByKind.Values.Sum();
}

/// <summary>Storage the AI Helper needs: comparables from real listings, ad benchmarks, the advice/feedback log and the learning observations.</summary>
public interface IAiHelperStore
{
    Task<List<AiComparable>> GetPjComparablesAsync(string provinceId, int take);
    Task<List<AiComparable>> GetVgComparablesAsync(int take);
    Task<Dictionary<string, (int Count, double Avg, double Min, double Max)>> GetSpeciesPriceStatsAsync();
    Task<AiAdBenchmark> GetAdBenchmarkAsync(int userId, string? category);
    Task<long> InsertAdviceAsync(AdviceLogEntry entry);
    Task<bool> SetFeedbackAsync(long adviceId, int userId, int feedback, string? comment);
    Task<HarvestResult> HarvestObservationsAsync(decimal usdToZar, SpeciesCatalog catalog);
    Task<List<string>> ExportObservationsJsonlAsync();
    Task<List<string>> ExportAdviceLogJsonlAsync(int? minFeedback);
}

/// <summary>
/// Raw ADO.NET implementation (same style as the other *_db classes). Every read that feeds the advisor is best-effort:
/// a missing table/column must never break an advice request, so callers wrap these and fall back to "no comparables".
/// </summary>
public class AiHelper_db : IAiHelperStore
{
    private readonly string _connectionString;

    // Plausible USD ranges per kind. Anything outside is treated as bad/mis-scaled data and is not harvested.
    private static readonly Dictionary<string, (double Lo, double Hi)> Plausible = new()
    {
        [PriceKinds.PjPackage] = (100, 80_000),
        [PriceKinds.PhRate] = (40, 1_500),
        [PriceKinds.FarmDailyRate] = (8, 1_500),
        [PriceKinds.SpeciesFee] = (15, 60_000),
    };

    // Nulls MUST be omitted: the Python trainer treats an explicit null differently from a missing key.
    private static readonly JsonSerializerOptions RawJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public AiHelper_db(string connectionString) => _connectionString = connectionString;

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ------------------------------------------------------------------ comparables

    /// <summary>Recent active packages, same-province first. Prices are as listed (the PJ package builder quotes USD); no owner identity is returned.</summary>
    public async Task<List<AiComparable>> GetPjComparablesAsync(string provinceId, int take)
    {
        using var conn = await OpenAsync();
        const string q = @"
            SELECT TOP (@take) p.pTitle, p.pStockPrice, p.pDurationDays, ISNULL(a.addressRegion, '')
            FROM packages p
            LEFT JOIN farm f ON p.pFarmId = f.farmId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            WHERE ISNULL(p.pStatus, 'Active') <> 'Inactive' AND p.pStockPrice > 0
            ORDER BY CASE WHEN REPLACE(REPLACE(LOWER(ISNULL(a.addressRegion, '')), ' ', '_'), '-', '_') = @prov THEN 0 ELSE 1 END,
                     p.pCreatedTimestamp DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@prov", provinceId);
        var list = new List<AiComparable>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AiComparable
            {
                Kind = "PJ_PACKAGE",
                Title = r.IsDBNull(0) ? "" : r.GetString(0),
                Price = r.GetDecimal(1),
                PriceNote = "USD, soos gelys (from-prys)",
                DurationDays = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)),
                Province = r.IsDBNull(3) ? null : r.GetString(3)
            });
        }
        return list;
    }

    /// <summary>Recent videographer packages. The platform does not label their currency or unit (per day vs total), so the note says so.</summary>
    public async Task<List<AiComparable>> GetVgComparablesAsync(int take)
    {
        using var conn = await OpenAsync();
        const string q = @"SELECT TOP (@take) vp.vgpTitle, vp.vgpPrice, ISNULL(vp.vgpRegionCovered, '')
                           FROM vgPackages vp WHERE vp.vgpPrice > 0 ORDER BY vp.vgPackageId DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<AiComparable>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AiComparable
            {
                Kind = "VG_PACKAGE",
                Title = r.IsDBNull(0) ? "" : r.GetString(0),
                Price = r.GetDecimal(1),
                PriceNote = "bedrag soos gelys - geen geldeenheid/eenheid op die platform gemerk nie",
                Province = r.IsDBNull(2) ? null : r.GetString(2)
            });
        }
        return list;
    }

    /// <summary>Per-species price statistics across all farms' availableAnimals rows (amounts as listed; the table has no currency column).</summary>
    public async Task<Dictionary<string, (int Count, double Avg, double Min, double Max)>> GetSpeciesPriceStatsAsync()
    {
        using var conn = await OpenAsync();
        const string q = @"SELECT CAST(aaName AS NVARCHAR(200)), COUNT(*), AVG(CAST(aaEntityPrice AS FLOAT)),
                                  MIN(CAST(aaEntityPrice AS FLOAT)), MAX(CAST(aaEntityPrice AS FLOAT))
                           FROM availableAnimals WHERE aaEntityPrice > 0 AND aaName IS NOT NULL
                           GROUP BY CAST(aaName AS NVARCHAR(200))";
        using var cmd = new SqlCommand(q, conn);
        var dict = new Dictionary<string, (int, double, double, double)>(StringComparer.OrdinalIgnoreCase);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            dict[r.GetString(0)] = (r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4));
        return dict;
    }

    /// <summary>Advertiser benchmark from REAL adViews/adClicks: the caller's own ads vs the platform and the chosen category.</summary>
    public async Task<AiAdBenchmark> GetAdBenchmarkAsync(int userId, string? category)
    {
        using var conn = await OpenAsync();
        var bench = new AiAdBenchmark { Category = category };

        const string ownQ = @"
            SELECT CAST(a.adTitle AS NVARCHAR(300)), CAST(a.adCategory AS NVARCHAR(100)), ISNULL(a.adViews, 0), ISNULL(a.adClicks, 0)
            FROM advertisements a
            JOIN ap ON a.adApId = ap.apId
            JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId
            WHERE ur.urUserId = @uid";
        using (var cmd = new SqlCommand(ownQ, conn))
        {
            cmd.Parameters.AddWithValue("@uid", userId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                long views = r.GetInt32(2), clicks = r.GetInt32(3);
                bench.OwnAds.Add(new AiOwnAd
                {
                    Title = r.IsDBNull(0) ? "" : r.GetString(0),
                    Category = r.IsDBNull(1) ? null : r.GetString(1),
                    Views = views,
                    Clicks = clicks,
                    ClickThroughPercent = views > 0 ? Math.Round(100.0 * clicks / views, 2) : null
                });
            }
        }
        bench.OwnAdCount = bench.OwnAds.Count;
        bench.OwnViews = bench.OwnAds.Sum(a => a.Views);
        bench.OwnClicks = bench.OwnAds.Sum(a => a.Clicks);
        bench.OwnClickThroughPercent = bench.OwnViews > 0 ? Math.Round(100.0 * bench.OwnClicks / bench.OwnViews, 2) : null;

        const string platQ = @"SELECT CAST(ISNULL(adCategory, '') AS NVARCHAR(100)), COUNT(*), SUM(CAST(ISNULL(adViews, 0) AS BIGINT)), SUM(CAST(ISNULL(adClicks, 0) AS BIGINT))
                               FROM advertisements GROUP BY CAST(ISNULL(adCategory, '') AS NVARCHAR(100))";
        using (var cmd = new SqlCommand(platQ, conn))
        {
            long allViews = 0, allClicks = 0;
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                string cat = r.GetString(0);
                int n = r.GetInt32(1);
                long v = r.GetInt64(2), c = r.GetInt64(3);
                allViews += v; allClicks += c;
                if (category != null && string.Equals(cat, category, StringComparison.OrdinalIgnoreCase))
                {
                    bench.CategoryAdCount = n;
                    bench.CategoryClickThroughPercent = v > 0 ? Math.Round(100.0 * c / v, 2) : null;
                }
            }
            bench.PlatformClickThroughPercent = allViews > 0 ? Math.Round(100.0 * allClicks / allViews, 2) : null;
        }
        return bench;
    }

    // ------------------------------------------------------------------ advice log + feedback

    public async Task<long> InsertAdviceAsync(AdviceLogEntry e)
    {
        using var conn = await OpenAsync();
        const string q = @"
            INSERT INTO aiAdviceLog (userId, roleCode, kind, requestJson, responseJson, userPriceUsd, p10Usd, p50Usd, p90Usd,
                                     modelVersion, llmProvider, llmModel, narrativeStatus, latencyMs)
            VALUES (@uid, @role, @kind, @ctx, @narr, @user, @p10, @p50, @p90, @mv, @prov, @model, @status, @lat);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@uid", e.UserId);
        cmd.Parameters.AddWithValue("@role", e.RoleCode);
        cmd.Parameters.AddWithValue("@kind", (object?)e.Kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ctx", e.ContextJson);
        cmd.Parameters.AddWithValue("@narr", (object?)e.NarrativeJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user", (object?)e.UserPriceUsd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p10", (object?)e.P10Usd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p50", (object?)e.P50Usd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p90", (object?)e.P90Usd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mv", (object?)e.ModelVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prov", (object?)e.LlmProvider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@model", (object?)e.LlmModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", e.NarrativeStatus);
        cmd.Parameters.AddWithValue("@lat", e.LatencyMs);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Records thumbs-up (+1) / thumbs-down (-1). Only the user the advice was produced for can rate it.</summary>
    public async Task<bool> SetFeedbackAsync(long adviceId, int userId, int feedback, string? comment)
    {
        using var conn = await OpenAsync();
        const string q = @"UPDATE aiAdviceLog SET feedback = @fb, feedbackComment = @c, feedbackAt = SYSUTCDATETIME()
                           WHERE adviceId = @id AND userId = @uid";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@fb", (short)feedback);
        cmd.Parameters.AddWithValue("@c", (object?)comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", adviceId);
        cmd.Parameters.AddWithValue("@uid", userId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ------------------------------------------------------------------ learning harvest

    private sealed record Observation(string Kind, PriceRawInput Raw, double TargetUsd, double Weight, string Source, string SourceRef);

    /// <summary>
    /// Turns the platform's own listings into training observations (idempotent upsert keyed on kind+source+sourceRef, so a
    /// changed listing price simply updates its row). Best-effort per kind: a missing table/column skips that kind only.
    /// Listings are ASKING prices with imperfectly-known currency/semantics, so they are down-weighted (0.25-0.5) and
    /// range-filtered; the trainer's real-share formula and its "prior_error_on_real" report guard against over-trusting them.
    /// </summary>
    public async Task<HarvestResult> HarvestObservationsAsync(decimal usdToZar, SpeciesCatalog catalog)
    {
        var result = new HarvestResult();
        var all = new List<Observation>();
        double fx = (double)usdToZar;

        using var conn = await OpenAsync();
        foreach (var (name, collect) in new (string, Func<Task<List<Observation>>>)[]
        {
            ("PJ_PACKAGE", () => CollectPackagesAsync(conn)),
            ("PH_RATE", () => CollectPhRatesAsync(conn, fx)),
            ("FARM_DAILY_RATE", () => CollectFarmRatesAsync(conn, fx)),
            ("SPECIES_FEE", () => CollectSpeciesFeesAsync(conn, fx, catalog)),
        })
        {
            try { all.AddRange(await collect()); }
            catch (Exception ex) { result.Skipped.Add($"{name}: {ex.Message}"); }
        }

        foreach (var o in all)
        {
            if (!Plausible.TryGetValue(o.Kind, out var range) || o.TargetUsd < range.Lo || o.TargetUsd > range.Hi) continue;
            const string q = @"
                UPDATE aiMarketObservations SET rawJson = @raw, targetUsd = @t, weight = @w, observedAt = SYSUTCDATETIME()
                 WHERE kind = @k AND source = @s AND sourceRef = @r;
                IF @@ROWCOUNT = 0
                    INSERT INTO aiMarketObservations (kind, rawJson, targetUsd, weight, source, sourceRef) VALUES (@k, @raw, @t, @w, @s, @r);";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@raw", JsonSerializer.Serialize(o.Raw, RawJson));
            cmd.Parameters.AddWithValue("@t", Math.Round(o.TargetUsd, 2));
            cmd.Parameters.AddWithValue("@w", o.Weight);
            cmd.Parameters.AddWithValue("@k", o.Kind);
            cmd.Parameters.AddWithValue("@s", o.Source);
            cmd.Parameters.AddWithValue("@r", o.SourceRef);
            await cmd.ExecuteNonQueryAsync();
            result.UpsertedByKind[o.Kind] = result.UpsertedByKind.GetValueOrDefault(o.Kind) + 1;
        }
        return result;
    }

    /// <summary>PJ packages. Assumes the builder's convention (USD, price per hunter for pDurationDays) - the drift report in the trainer will reveal it if that is wrong.</summary>
    private static async Task<List<Observation>> CollectPackagesAsync(SqlConnection conn)
    {
        const string q = @"
            SELECT p.packageId, p.pStockPrice, p.pDurationDays, p.pAvailableFrom, ISNULL(a.addressRegion, '')
            FROM packages p
            LEFT JOIN farm f ON p.pFarmId = f.farmId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            WHERE p.pStockPrice > 0 AND p.pDurationDays > 0 AND ISNULL(p.pStatus, 'Active') <> 'Inactive'";
        var list = new List<Observation>();
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var raw = new PriceRawInput
            {
                Province = r.IsDBNull(4) ? null : r.GetString(4),
                Month = r.IsDBNull(3) ? null : r.GetDateTime(3).Month,
                Market = "international",
                DurationDays = Convert.ToDouble(r.GetValue(2))
            };
            list.Add(new Observation(PriceKinds.PjPackage, raw, (double)r.GetDecimal(1), 0.25, "platform_listing", $"package:{r.GetInt32(0)}"));
        }
        return list;
    }

    /// <summary>PH day rates set by real PHs (ZAR). The schema default (R2,800 with no dangerous-game rate) is skipped as "never set by the PH".</summary>
    private static async Task<List<Observation>> CollectPhRatesAsync(SqlConnection conn, double fx)
    {
        using (var chk = new SqlCommand(@"SELECT CASE WHEN COL_LENGTH('ph','phStandardDailyRate') IS NOT NULL
                                              AND COL_LENGTH('ph','phDangerousGameDailyRate') IS NOT NULL
                                              AND COL_LENGTH('ph','phHasDangerousGame') IS NOT NULL THEN 1 ELSE 0 END", conn))
        {
            if ((int)(await chk.ExecuteScalarAsync())! == 0) throw new InvalidOperationException("ph rate columns do not exist yet");
        }

        const string q = @"
            SELECT ph.phId, ISNULL(ph.phYearsOfExperience, 0), ph.phStandardDailyRate, ph.phDangerousGameDailyRate, ISNULL(ph.phHasDangerousGame, 0),
                   (SELECT COUNT(*) FROM bookings b WHERE b.bookPhId = ph.phId AND b.bookStatus = 'Completed')
            FROM ph WHERE ph.phStandardDailyRate > 0";
        var list = new List<Observation>();
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int id = r.GetInt32(0);
            double years = Convert.ToDouble(r.GetValue(1));
            decimal std = r.GetDecimal(2);
            decimal? dg = r.IsDBNull(3) ? null : r.GetDecimal(3);
            bool accredited = r.GetBoolean(4);
            double hunts = Convert.ToDouble(r.GetValue(5));

            bool untouchedDefault = std == 2800m && dg is null;
            if (!untouchedDefault)
                list.Add(new Observation(PriceKinds.PhRate, new PriceRawInput
                {
                    Market = "domestic", YearsExperience = years, HuntsCompleted = hunts, DgAccredited = accredited, IsDgRate = false
                }, (double)std / fx, 0.5, "platform_listing", $"ph:{id}:std"));

            if (dg is > 0 && accredited)
                list.Add(new Observation(PriceKinds.PhRate, new PriceRawInput
                {
                    Market = "domestic", YearsExperience = years, HuntsCompleted = hunts, DgAccredited = true, IsDgRate = true
                }, (double)dg.Value / fx, 0.5, "platform_listing", $"ph:{id}:dg"));
        }
        return list;
    }

    /// <summary>Farm lodge daily rates (ZAR). The farm-creation default (R850) is skipped as "never set by the farmer".</summary>
    private static async Task<List<Observation>> CollectFarmRatesAsync(SqlConnection conn, double fx)
    {
        const string q = @"
            SELECT f.farmId, CAST(f.farmSize AS FLOAT), ISNULL(a.addressRegion, ''), fl.flPrice, ISNULL(fl.flRooms, 0),
                   ISNULL(fl.flCateringIncluded, 1), CAST(ISNULL(fl.flFacilities, '') AS NVARCHAR(1000)),
                   CAST(ISNULL(f.farmFencingType, '') AS NVARCHAR(200)),
                   (SELECT COUNT(*) FROM availableAnimals aa WHERE aa.aaFarmId = f.farmId)
            FROM farm f
            JOIN farmLodge fl ON fl.flFarmId = f.farmId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            WHERE fl.flPrice > 0 AND fl.flPrice <> 850";
        var list = new List<Observation>();
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string facilities = r.GetString(6).ToLowerInvariant();
            string fencing = r.GetString(7).ToLowerInvariant();
            var raw = new PriceRawInput
            {
                Province = r.GetString(2),
                Month = DateTime.UtcNow.Month,
                Market = "domestic",
                HuntersPerPh = 1,
                FarmSizeHa = r.IsDBNull(1) ? null : r.GetDouble(1),
                LodgeRooms = Convert.ToDouble(r.GetValue(4)),
                Catering = r.GetBoolean(5),
                ColdRoom = facilities.Contains("koelkamer") || facilities.Contains("cold"),
                Pool = facilities.Contains("swem") || facilities.Contains("pool"),
                Wifi = facilities.Contains("wifi") || facilities.Contains("wi-fi"),
                GuideIncluded = facilities.Contains("spoor") || facilities.Contains("gids") || facilities.Contains("guide"),
                NSpecies = Convert.ToDouble(r.GetValue(8)),
                GameFenced = fencing.Contains("wild") || fencing.Contains("game") || fencing.Contains("hoog")
            };
            list.Add(new Observation(PriceKinds.FarmDailyRate, raw, (double)r.GetDecimal(3) / fx, 0.4, "platform_listing", $"farm:{r.GetInt32(0)}"));
        }
        return list;
    }

    /// <summary>Per-species prices farms list (ZAR). The stored price is the trophy price when set, else the male price, so it is harvested as tier "trophy".</summary>
    private static async Task<List<Observation>> CollectSpeciesFeesAsync(SqlConnection conn, double fx, SpeciesCatalog catalog)
    {
        const string q = @"
            SELECT aa.aaId, CAST(aa.aaName AS NVARCHAR(200)), aa.aaEntityPrice, CAST(f.farmSize AS FLOAT), ISNULL(a.addressRegion, ''),
                   CAST(ISNULL(f.farmFencingType, '') AS NVARCHAR(200))
            FROM availableAnimals aa
            JOIN farm f ON aa.aaFarmId = f.farmId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            WHERE aa.aaEntityPrice > 0";
        var list = new List<Observation>();
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var sp = catalog.Resolve(r.IsDBNull(1) ? null : r.GetString(1));
            if (sp is null) continue;
            string fencing = r.GetString(5).ToLowerInvariant();
            var raw = new PriceRawInput
            {
                Species = sp.Id,
                Tier = "trophy",
                Province = r.GetString(4),
                Month = DateTime.UtcNow.Month,
                Market = "domestic",
                FarmSizeHa = r.IsDBNull(3) ? null : r.GetDouble(3),
                GameFenced = fencing.Contains("wild") || fencing.Contains("game") || fencing.Contains("hoog")
            };
            list.Add(new Observation(PriceKinds.SpeciesFee, raw, (double)r.GetDecimal(2) / fx, 0.3, "platform_listing", $"animal:{r.GetInt32(0)}"));
        }
        return list;
    }

    // ------------------------------------------------------------------ exports for the Python trainers

    /// <summary>All observations as JSONL in the trainer's --real format ({"kind","raw","target_usd","source","weight"}).</summary>
    public async Task<List<string>> ExportObservationsJsonlAsync()
    {
        using var conn = await OpenAsync();
        var lines = new List<string>();
        using var cmd = new SqlCommand("SELECT kind, rawJson, targetUsd, weight, source FROM aiMarketObservations ORDER BY observationId", conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var o = new JsonObject
            {
                ["kind"] = r.GetString(0),
                ["raw"] = JsonNode.Parse(r.GetString(1)),
                ["target_usd"] = (double)r.GetDecimal(2),
                ["weight"] = r.GetDouble(3),
                ["source"] = r.GetString(4)
            };
            lines.Add(o.ToJsonString());
        }
        return lines;
    }

    /// <summary>
    /// Logged advice as anonymised (no user id) pairs: {"role", "contextJson", "advice", "feedback", "modelVersion"}. Pass minFeedback=1 to
    /// export only advice users rated helpful - the examples to judge prompt or provider changes against.
    /// </summary>
    public async Task<List<string>> ExportAdviceLogJsonlAsync(int? minFeedback)
    {
        using var conn = await OpenAsync();
        var lines = new List<string>();
        string q = @"SELECT roleCode, requestJson, responseJson, feedback, modelVersion FROM aiAdviceLog
                     WHERE responseJson IS NOT NULL AND narrativeStatus = 'ok'"
                   + (minFeedback.HasValue ? " AND feedback >= @min" : "") + " ORDER BY adviceId";
        using var cmd = new SqlCommand(q, conn);
        if (minFeedback.HasValue) cmd.Parameters.AddWithValue("@min", (short)minFeedback.Value);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var o = new JsonObject
            {
                ["role"] = r.GetString(0),
                // The exact string the LLM was given (NOT re-serialised): re-encoding would change escaping and the
                // exported context would no longer be byte-identical to the production prompt.
                ["contextJson"] = r.GetString(1),
                ["advice"] = JsonNode.Parse(r.GetString(2)),
                ["feedback"] = r.IsDBNull(3) ? null : (int)r.GetInt16(3),
                ["modelVersion"] = r.IsDBNull(4) ? null : r.GetString(4)
            };
            lines.Add(o.ToJsonString());
        }
        return lines;
    }
}
