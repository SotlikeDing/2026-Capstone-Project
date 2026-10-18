using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WebApp.Models;

namespace WebApp.Services
{
    /// <summary>
    /// Calls WebAPI's AiHelperController (api/aihelper/*) with the signed-in user's bearer token. Kept separate from
    /// MikpuntApiService on purpose: its HttpClient needs a much longer timeout than the rest of the app, because an LLM answer
    /// can legitimately take a minute or more (the server enforces its own per-call limit and degrades to numbers-only).
    /// </summary>
    public class AiHelperClient
    {
        private readonly HttpClient _http;
        private readonly AuthService _auth;

        public AiHelperClient(HttpClient http, AuthService auth)
        {
            _http = http;
            _auth = auth;
        }

        private HttpRequestMessage Build(HttpMethod method, string url, object? body = null)
        {
            var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = JsonContent.Create(body);
            if (!string.IsNullOrWhiteSpace(_auth.Token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
            return req;
        }

        /// <summary>Asks the AI Helper to optimise the draft offer. Returns the (Afrikaans) reason on failure instead of throwing.</summary>
        public async Task<(bool Ok, AiOptimizeResponseModel? Result, string? Error)> OptimizeAsync(AiOptimizeRequestModel request, CancellationToken ct = default)
        {
            try
            {
                using var res = await _http.SendAsync(Build(HttpMethod.Post, "api/aihelper/optimize", request), ct);
                if (res.IsSuccessStatusCode)
                    return (true, await res.Content.ReadFromJsonAsync<AiOptimizeResponseModel>(cancellationToken: ct), null);

                return (false, null, await ReadError(res));
            }
            catch (TaskCanceledException) { return (false, null, "Die versoek het te lank geneem. Probeer asseblief weer."); }
            catch (HttpRequestException) { return (false, null, "Kon nie met die bediener kommunikeer nie."); }
        }

        /// <summary>Smart Teaming: ranked partner suggestions for the role. Returns the (Afrikaans) reason on failure instead of throwing.</summary>
        public async Task<(bool Ok, AiTeamResponseModel? Result, string? Error)> TeamMatchesAsync(AiTeamRequestModel request, CancellationToken ct = default)
        {
            try
            {
                using var res = await _http.SendAsync(Build(HttpMethod.Post, "api/aihelper/team-matches", request), ct);
                if (res.IsSuccessStatusCode)
                    return (true, await res.Content.ReadFromJsonAsync<AiTeamResponseModel>(cancellationToken: ct), null);

                return (false, null, await ReadError(res));
            }
            catch (TaskCanceledException) { return (false, null, "Die versoek het te lank geneem. Probeer asseblief weer."); }
            catch (HttpRequestException) { return (false, null, "Kon nie met die bediener kommunikeer nie."); }
        }

        /// <summary>Thumbs-up/down on a piece of advice; the ratings are exported to judge and improve the advice prompt.</summary>
        public async Task<bool> SendFeedbackAsync(long adviceId, bool helpful, string? comment = null)
        {
            try
            {
                using var res = await _http.SendAsync(Build(HttpMethod.Post, "api/aihelper/feedback", new { adviceId, helpful, comment }));
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static async Task<string> ReadError(HttpResponseMessage res)
        {
            switch (res.StatusCode)
            {
                case HttpStatusCode.Unauthorized: return "U sessie het verval. Teken asseblief weer in.";
                case HttpStatusCode.Forbidden: return "U het nie toegang tot die AI-helper vir hierdie rol nie.";
                case HttpStatusCode.TooManyRequests: return "Te veel versoeke - wag asseblief 'n minuut en probeer weer.";
            }
            try
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString()!;
            }
            catch { /* fall through */ }
            return "Kon nie 'n antwoord van die AI-helper kry nie.";
        }
    }
}
