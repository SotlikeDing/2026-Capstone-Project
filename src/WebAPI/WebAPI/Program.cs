using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using WebAPI.HealthChecks;
using WebAPI.Hubs;
using WebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers(options => options.Filters.Add<WebAPI.Services.HideServerErrorDetailsFilter>()); // Add controller services
builder.Services.AddSignalR(); // Add SignalR

// Register database services
var configuration = builder.Configuration;
var connectionString = configuration.GetConnectionString("DefaultConnection") 
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddScoped<WebAPI.Database.Address_db>       (_ => new WebAPI.Database.Address_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Ads_db>           (_ => new WebAPI.Database.Ads_db(connectionString, new WebAPI.Database.Address_db(connectionString)));
builder.Services.AddScoped<WebAPI.Database.Bookings_db>      (_ => new WebAPI.Database.Bookings_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.BookingCompanions_db> (_ => new WebAPI.Database.BookingCompanions_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Chats_db>         (_ => new WebAPI.Database.Chats_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Extra_db>         (_ => new WebAPI.Database.Extra_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Farmer_db>        (_ => new WebAPI.Database.Farmer_db(connectionString, new WebAPI.Database.Address_db(connectionString)));
builder.Services.AddScoped<WebAPI.Database.HuntingPermit_db> (_ => new WebAPI.Database.HuntingPermit_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Landing_db>       (_ => new WebAPI.Database.Landing_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Package_db>       (_ => new WebAPI.Database.Package_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.People_db>        (_ => new WebAPI.Database.People_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Permit_db>        (_ => new WebAPI.Database.Permit_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Permits_db>       (_ => new WebAPI.Database.Permits_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Profile_db>       (sp => new WebAPI.Database.Profile_db(connectionString, sp.GetRequiredService<ISystemSettingsCache>()));
builder.Services.AddScoped<WebAPI.Database.Rating_db>        (_ => new WebAPI.Database.Rating_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Story_db>         (_ => new WebAPI.Database.Story_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Team_db>          (_ => new WebAPI.Database.Team_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.User_db>          (_ => new WebAPI.Database.User_db(connectionString));
builder.Services.AddScoped<WebAPI.Services.IVerificationCodeStore>(_ => new WebAPI.Services.SqlVerificationCodeStore(connectionString));
builder.Services.AddSingleton<WebAPI.Services.IUserStatusCache>(_ => new WebAPI.Services.UserStatusCache(connectionString));
builder.Services.AddScoped<WebAPI.Database.Weapon_db>        (_ => new WebAPI.Database.Weapon_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.PhPricing_db>     (_ => new WebAPI.Database.PhPricing_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.VgPackages_db>    (_ => new WebAPI.Database.VgPackages_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.AdminAudit_db>        (_ => new WebAPI.Database.AdminAudit_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.FeatureFlags_db>      (_ => new WebAPI.Database.FeatureFlags_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.RoleVerifications_db> (_ => new WebAPI.Database.RoleVerifications_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.SystemSettings_db>    (_ => new WebAPI.Database.SystemSettings_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.UserDocuments_db>     (_ => new WebAPI.Database.UserDocuments_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.ResourceOwnership_db> (_ => new WebAPI.Database.ResourceOwnership_db(connectionString));
builder.Services.AddScoped<WebAPI.Database.Payments_db>          (_ => new WebAPI.Database.Payments_db(connectionString));

// Payments: one server-side price + credit path (PaymentService), PayFast checkout signing
// and notification checks (PayFastItnService). See Services/Payments.
builder.Services.Configure<WebAPI.Services.Payments.PayFastOptions>(configuration.GetSection("PayFast"));
builder.Services.AddHttpClient(WebAPI.Services.Payments.PayFastItnService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<WebAPI.Services.Payments.PaymentService>();
builder.Services.AddScoped<WebAPI.Services.Payments.PayFastItnService>();

// Register Email Service
builder.Services.AddScoped<WebAPI.Services.IEmailService, WebAPI.Services.EmailService>();

// AI Helper: trained price model (ONNX, in-process) + an LLM for the written advice. See Services/Ai and tools/ai.
builder.Services.Configure<WebAPI.Services.Ai.AiHelperOptions>(configuration.GetSection(WebAPI.Services.Ai.AiHelperOptions.SectionName));
builder.Services.AddScoped<WebAPI.Database.IAiHelperStore>(_ => new WebAPI.Database.AiHelper_db(connectionString));
builder.Services.AddSingleton<WebAPI.Services.Ai.IPriceModel, WebAPI.Services.Ai.PriceModelService>();
// The LLM is a hosted API reached with a key (AiHelper:Llm:Provider + ApiKey, optional Fallback) - nothing is self-hosted.
builder.Services.AddHttpClient("ai-llm", c => c.Timeout = Timeout.InfiniteTimeSpan);      // the clients enforce their own per-call timeout
builder.Services.AddSingleton<WebAPI.Services.Ai.ILlmClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var llmOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebAPI.Services.Ai.AiHelperOptions>>().Value.Llm;
    return WebAPI.Services.Ai.LlmClientFactory.Create(llmOptions, () => factory.CreateClient("ai-llm"));
});
builder.Services.AddScoped<WebAPI.Services.Ai.IPackageAdvisor, WebAPI.Services.Ai.PackageAdvisorService>();
builder.Services.AddScoped<WebAPI.Database.ITeamMatchStore>(_ => new WebAPI.Database.AiTeam_db(connectionString));
builder.Services.AddScoped<WebAPI.Services.Ai.ITeamMatcher, WebAPI.Services.Ai.TeamMatchService>();
builder.Services.AddHostedService<WebAPI.Services.Ai.AiLearningService>();

// Shared, in-memory (per-instance) token/session/settings state used below.
builder.Services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddSingleton<WebAPI.Services.IFeatureFlags>(sp => new WebAPI.Services.FeatureFlagsCache(connectionString,
    sp.GetService<ILogger<WebAPI.Services.FeatureFlagsCache>>()));
builder.Services.AddSingleton<ISystemSettingsCache, SystemSettingsCache>();

// CORS: explicit origin allowlist with AllowCredentials. Auth is bearer-JWT only (never
// cookies), so classic CSRF - which relies on a browser auto-attaching ambient cookie
// credentials to a cross-site request - does not apply here: a malicious page has no way
// to obtain or attach the victim's Authorization header. Methods/headers are kept to an
// explicit allowlist (rather than AllowAnyMethod/AllowAnyHeader) purely to minimise
// cross-origin attack surface further.
builder.Services.AddCors(options =>
{
    Action<Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder> configurePolicy = policy =>
    {
        policy.WithOrigins(WebAPI.Services.WebAppOrigins.All)
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization")
            .AllowCredentials();
    };

    options.AddPolicy("AllowBlazor", configurePolicy);
    options.AddPolicy("AllowBlazorApp", configurePolicy);
});

// JWT Bearer authentication - validates tokens minted by UserController.GenerateJwtToken.
var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Configuration 'Jwt:Key' not found.");
// The repo's appsettings key is public: anyone could mint an admin token with it. Outside
// Development the app must be given its own secret (Jwt__Key, 32+ random characters).
if (!builder.Environment.IsDevelopment() && (jwtKey.Length < 32 || jwtKey.StartsWith("DEV-ONLY-PLACEHOLDER", StringComparison.Ordinal)))
    throw new InvalidOperationException("Jwt:Key is the public placeholder or too short. Set Jwt__Key to a private 32+ character secret.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            // SignalR's WebSocket/SSE transports can't always set an Authorization header,
            // so the client sends the JWT as ?access_token=... - accept it on the hub paths only.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken)
                    && (path.StartsWithSegments("/chathub") || path.StartsWithSegments("/api/chathub")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var blacklist = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                // .NET 8's JwtBearer hands over a JsonWebToken (not the older JwtSecurityToken),
                // so read the raw token from either - otherwise logout revocation is never checked.
                var rawToken = context.SecurityToken switch
                {
                    Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.EncodedToken,
                    System.IdentityModel.Tokens.Jwt.JwtSecurityToken legacy => legacy.RawData,
                    _ => null
                };
                if (!string.IsNullOrEmpty(rawToken) && blacklist.IsRevoked(rawToken))
                {
                    context.Fail("Token has been revoked.");
                    return;
                }

                // A suspension must take effect immediately, not when the (24h) token expires.
                if (int.TryParse(context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var tokenUserId)
                    && await context.HttpContext.RequestServices.GetRequiredService<IUserStatusCache>().IsSuspendedAsync(tokenUserId))
                {
                    context.Fail("Account is suspended.");
                    return;
                }

                // Role codes are stored inconsistently-cased in the DB (e.g. "ADMIN").
                // [Authorize(Roles = "Admin")] does an ordinal (case-sensitive) match, so
                // add a canonical "Admin" role claim whenever any casing variant is present.
                if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    bool hasCanonicalAdmin = identity.Claims.Any(c => c.Type == identity.RoleClaimType && c.Value == "Admin");
                    bool hasAnyCaseAdmin = identity.Claims.Any(c => c.Type == identity.RoleClaimType && string.Equals(c.Value, "Admin", StringComparison.OrdinalIgnoreCase));
                    if (!hasCanonicalAdmin && hasAnyCaseAdmin)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim(identity.RoleClaimType, "Admin"));
                    }
                }

            }
        };
    })
    // Service-to-service calls from MobileAPI (team ratings from the app). Only
    // endpoints that name InternalServiceAuthenticationHandler.BearerOrInternal accept it.
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
        InternalServiceAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();

// WebAPI -> MobileAPI: VG booking media uploads/listing from the website are relayed to
// MobileAPI, which owns the private media storage and the recap pipeline.
builder.Services.Configure<InternalApiOptions>(configuration.GetSection(InternalApiOptions.SectionName));
builder.Services.AddHttpClient(WebAPI.Controllers.VgBookingMediaController.HttpClientName, (sp, client) =>
{
    var baseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InternalApiOptions>>().Value.MobileApiBaseUrl;
    if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Rate limiting on admin endpoints (Microsoft.AspNetCore.RateLimiting - built into the
// ASP.NET Core 8 shared framework, no extra NuGet package needed).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AdminPolicy", httpContext =>
    {
        string partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Read the limit live from the settings cache on every request (rather than
        // baking a literal in at AddRateLimiter registration time) so an admin
        // changing AdminRateLimitPermitLimit/WindowSeconds takes effect immediately.
        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("AdminRateLimitPermitLimit", 30);
        int windowSeconds = settingsCache.GetInt("AdminRateLimitWindowSeconds", 10);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Rating submission is per-authenticated-user (not per-IP) since it always
    // requires a bearer token - a compromised/scripted account shouldn't be able to
    // flood ratings just by rotating IPs.
    options.AddPolicy("RatingPolicy", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("RatingRateLimitPermitLimit", 10);
        int windowSeconds = settingsCache.GetInt("RatingRateLimitWindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // The authenticated "rate your team" read (api/rating/booking/{id}/team). Its callers
    // arrive via the WebApp and MobileAPI servers, so an IP partition would lump every
    // user together - partition by the authenticated user instead (the limiter runs after
    // authorization, so relayed internal-service callers carry their user id too).
    options.AddPolicy("RatingTeamReadPolicy", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromSeconds(60),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // The two rating GET endpoints are deliberately unauthenticated (ratings are
    // public, same as the profile they're shown on) - but "unauthenticated" doesn't
    // mean "unthrottled". Partitioned by IP since there's no reliable caller identity.
    options.AddPolicy("RatingReadPolicy", httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("RatingReadRateLimitPermitLimit", 60);
        int windowSeconds = settingsCache.GetInt("RatingReadRateLimitWindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Document upload/replace/delete is per-authenticated-user - a compromised or
    // scripted account shouldn't be able to flood the (comparatively expensive,
    // base64-file-carrying) document endpoints.
    options.AddPolicy("DocumentPolicy", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("DocumentRateLimitPermitLimit", 20);
        int windowSeconds = settingsCache.GetInt("DocumentRateLimitWindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // AI Helper requests are slow (an LLM call can take a minute) and, for the Claude provider, cost money - throttle
    // per authenticated user so one account can't run up the bill or saturate the local GPU.
    options.AddPolicy("AiPolicy", httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("AiRateLimitPermitLimit", 6);
        int windowSeconds = settingsCache.GetInt("AiRateLimitWindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // The document requirements/summary GETs are deliberately unauthenticated (shown
    // on public profiles and registration forms) - but that shouldn't mean unthrottled
    // enumeration of arbitrary userRoleIds. Partitioned by IP since there's no reliable
    // caller identity, same as RatingReadPolicy.
    options.AddPolicy("DocumentReadPolicy", httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var settingsCache = httpContext.RequestServices.GetRequiredService<ISystemSettingsCache>();
        int permitLimit = settingsCache.GetInt("DocumentReadRateLimitPermitLimit", 60);
        int windowSeconds = settingsCache.GetInt("DocumentReadRateLimitWindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// Behind the NetBird proxy and Docker the socket address is the proxy's; trust X-Forwarded-For from
// those private networks only, so per-IP throttling sees the real client.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
});

builder.Services.AddMemoryCache();

// Anonymous account endpoints: per-IP limits everywhere, per-address limits where we send e-mail.
// (Sign-in already has its own per-account lockout in UserController.)
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    int perIp = config.GetValue("AuthThrottle:PerIpPerMinute", 30);
    var rules = new WebAPI.Services.AuthThrottleRules();
    foreach (var path in new[] { "/api/user/signin", "/api/user/register", "/api/user/refreshToken", "/api/user/verifyEmail",
                                 "/api/user/resendVerificationEmail", "/api/user/validateResetToken", "/api/user/forgotPassword",
                                 "/api/user/resetPassword", "/api/contact" })
        rules.PerIp[path] = (perIp, TimeSpan.FromMinutes(1));
    int perMail = config.GetValue("AuthThrottle:MailsPerAddressPer15Minutes", 3);
    foreach (var path in new[] { "/api/user/forgotPassword", "/api/user/resendVerificationEmail" })
        rules.PerEmail[path] = (perMail, TimeSpan.FromMinutes(15));
    return rules;
});

// Health checks: real DB ping, surfaced at /health and via /api/admin/health.
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("database");

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError;
        context.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;

        var exceptionHandlerPathFeature =
            context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();

        if (exceptionHandlerPathFeature?.Error is not null)
        {
            var ex = exceptionHandlerPathFeature.Error;
            Console.WriteLine($"[Global Error] {context.TraceIdentifier} {ex}");
            // Only Development shows the exception; elsewhere SQL/table details must not reach the client.
            if (app.Environment.IsDevelopment())
                await context.Response.WriteAsJsonAsync(new { message = ex.Message, type = ex.GetType().Name });
            else
                await context.Response.WriteAsJsonAsync(new { message = "Iets het op die bediener fout gegaan. Probeer asseblief weer.", traceId = context.TraceIdentifier });
        }
    });
});

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    // Path only: query strings carry e-mail addresses and verification/reset tokens.
    Console.WriteLine($"Start processing HTTP request {context.Request.Method} {context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
    await next(context);
});

app.UseMiddleware<WebAPI.Services.AuthThrottleMiddleware>();

app.UseCors("AllowBlazor");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// In Docker the proxy terminates HTTPS and the container only speaks HTTP; redirect only when an HTTPS port is set.
if (!string.IsNullOrWhiteSpace(app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"] ?? app.Configuration["HTTPS_PORTS"]))
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<WebAPI.Services.MaintenanceModeMiddleware>();   // admin flag Enable_Maintenance_Mode
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers(); // Map controller routes
app.MapHealthChecks("/health");
app.MapHub<ChatHub>("/chathub"); // Map ChatHub
app.MapHub<ChatHub>("/api/chathub"); // Map ChatHub under /api prefix as well

// Initialize database
using (var scope = app.Services.CreateScope())
{
    // Use the configured Mikpunt database
    var userDb = scope.ServiceProvider.GetRequiredService<WebAPI.Database.User_db>();

    // Auto-provision schema on a fresh database and keep it current on every deploy -
    // lets a CI/CD pipeline point this app at a brand new, empty SQL Server with no
    // manual sqlcmd step. Must run before the settings-cache preload below, which
    // depends on the systemSettings table this creates. See DatabaseBootstrapper.
    // A failed migration throws and stops startup (see DatabaseBootstrapper), unless
    // Database:ContinueOnMigrationFailure is set for an emergency start.
    await WebAPI.Services.DatabaseBootstrapper.RunAsync(connectionString,
        app.Configuration.GetValue("Database:ContinueOnMigrationFailure", false));

    // Load system settings (pricing + operational constants) into the in-memory
    // cache so every consumer (rate limiter, JWT/lockout logic, pricing pages) has
    // live values from first request onward.
    try
    {
        var settingsDb = scope.ServiceProvider.GetRequiredService<WebAPI.Database.SystemSettings_db>();
        var settingsCache = scope.ServiceProvider.GetRequiredService<ISystemSettingsCache>();
        await settingsCache.RefreshAsync(settingsDb);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SYSTEM SETTINGS] Could not preload settings cache at startup: {ex.Message}");
    }
}

app.Run();

// Exposes the top-level Program class so WebApplicationFactory<Program> can be used
// from the test project.
public partial class Program { }

