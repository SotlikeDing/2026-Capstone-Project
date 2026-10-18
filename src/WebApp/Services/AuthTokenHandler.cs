using System.Diagnostics;
using System.Net.Http.Headers;

namespace WebApp.Services
{
    /// <summary>
    /// Adds the signed-in user's JWT as a Bearer token to every WebAPI request made through
    /// the shared HttpClient, so individual MikpuntApiService methods no longer have to
    /// remember to call AttachAuthHeader. A request that already carries an Authorization
    /// header is left untouched. Anonymous visitors (no token) send no header, which the
    /// WebAPI's [AllowAnonymous] public endpoints accept.
    ///
    /// It also logs every call (category WebApp.Services.AuthTokenHandler): failures and
    /// error statuses as warnings, the rest at Debug. The client is built per circuit by
    /// hand (an IHttpClientFactory handler would get another scope's AuthService), so it
    /// doesn't get the factory's built-in request logging.
    /// </summary>
    public class AuthTokenHandler : DelegatingHandler
    {
        private readonly AuthService _auth;
        private readonly ILogger<AuthTokenHandler>? _logger;
        private readonly IHttpContextAccessor? _httpContext;

        public AuthTokenHandler(AuthService auth, ILogger<AuthTokenHandler>? logger = null, IHttpContextAccessor? httpContext = null) : base(new HttpClientHandler())
        {
            _auth = auth;
            _logger = logger;
            _httpContext = httpContext;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization == null && !string.IsNullOrWhiteSpace(_auth.Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
            }

            // The WebAPI sees every website call coming from this container. Pass on the visitor's address
            // (the circuit's connection, already resolved through the proxy's X-Forwarded-For) so per-IP
            // limits such as "15 sign-ins a minute" apply per visitor, not to the whole site at once.
            var visitorIp = _httpContext?.HttpContext?.Connection.RemoteIpAddress;
            if (visitorIp != null && !request.Headers.Contains("X-Forwarded-For"))
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", visitorIp.ToString());

            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            var timer = Stopwatch.StartNew();
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                // Our own token refused: suspended, revoked or expired - end the session in this browser.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    && request.Headers.Authorization?.Parameter is { Length: > 0 } sent && sent == _auth.Token
                    && !path.Contains("/api/user/signin", StringComparison.OrdinalIgnoreCase))
                {
                    _auth.NotifySessionRejected();
                }
                if (_logger != null)
                {
                    int status = (int)response.StatusCode;
                    if (status >= 400)
                        _logger.LogWarning("WebAPI {Method} {Path} -> {Status} in {Elapsed} ms", request.Method, path, status, timer.ElapsedMilliseconds);
                    else
                        _logger.LogDebug("WebAPI {Method} {Path} -> {Status} in {Elapsed} ms", request.Method, path, status, timer.ElapsedMilliseconds);
                }
                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "WebAPI {Method} {Path} failed after {Elapsed} ms", request.Method, path, timer.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
