using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MobileApi.Database;
using MobileApi.Options;
using MobileApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.Filters.Add<HideServerErrorDetailsFilter>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the Mikpunt access token returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("youtube", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

var configuredYoutubeKeyPath = builder.Configuration["Youtube:DataProtectionKeysPath"]
    ?? Path.Combine("Data", "YoutubeKeys");
var youtubeKeyPath = Path.IsPathRooted(configuredYoutubeKeyPath)
    ? configuredYoutubeKeyPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredYoutubeKeyPath);
Directory.CreateDirectory(youtubeKeyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(youtubeKeyPath))
    .SetApplicationName("Mikpunt.MobileAPI");

var configuredPhotoPath = builder.Configuration["TrophyPhotos:StoragePath"]
    ?? Path.Combine("Data", "TrophyPhotos");
var trophyPhotoPath = Path.IsPathRooted(configuredPhotoPath)
    ? configuredPhotoPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredPhotoPath);
Directory.CreateDirectory(trophyPhotoPath);
builder.Services.AddSingleton<ITrophyPhotoStorage>(
    new TrophyPhotoStorage(trophyPhotoPath)
);

builder.Services.Configure<RowlandWardOptions>(
    builder.Configuration.GetSection(RowlandWardOptions.SectionName)
);
builder.Services.Configure<SharedBookingOptions>(builder.Configuration.GetSection(SharedBookingOptions.SectionName));
builder.Services.Configure<YoutubeOptions>(builder.Configuration.GetSection(YoutubeOptions.SectionName));
builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection(InternalApiOptions.SectionName));
builder.Services.AddHttpClient("webapi-internal", (sp, client) =>
{
    var baseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InternalApiOptions>>().Value.WebApiBaseUrl;
    if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<IVideoProbe, FfprobeVideoProbe>();
builder.Services.AddSingleton<IRowlandWardCatalogueService, RowlandWardCatalogueService>();
builder.Services.AddSingleton<IRowlandWardSpeciesResolver, RowlandWardSpeciesResolver>();
builder.Services.AddSingleton<IRowlandWardEvaluationService, RowlandWardEvaluationService>();
builder.Services.AddScoped<ILegacyTrophySpeciesAuditService, LegacyTrophySpeciesAuditService>();
builder.Services.AddScoped<IMobileAccessService, MobileAccessService>();
builder.Services.AddScoped<ISharedBookingRepository, SharedBookingRepository>();
builder.Services.AddScoped<ITrophySubmissionsRepository, TrophySubmissionsRepository>();
builder.Services.AddScoped<IRecapJobStore, RecapJobStore>();
builder.Services.AddScoped<IYoutubePublishStore, YoutubePublishStore>();
builder.Services.AddScoped<IMediaCleanupStore, MediaCleanupStore>();
builder.Services.AddSingleton<ISharedBookingStorage, SharedBookingStorage>();
builder.Services.AddSingleton<IRecapRenderer, FfmpegRecapRenderer>();
builder.Services.AddHttpClient<YoutubeOAuthService>();
builder.Services.AddHostedService<RecapProcessingWorker>();
builder.Services.AddHostedService<YoutubePublishWorker>();
builder.Services.AddHostedService<MediaCleanupWorker>();

builder.Services.AddScoped<Chats_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new Chats_db(connectionString!);
});

builder.Services.AddScoped<Users_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new Users_db(connectionString!);
});
builder.Services.AddScoped<IUserProfilePreferencesRepository>(sp =>
    sp.GetRequiredService<Users_db>()
);
builder.Services.AddScoped<MobileBookings_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new MobileBookings_db(connectionString!);
});
builder.Services.AddScoped<FarmRatings_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new FarmRatings_db(connectionString!);
});
builder.Services.AddScoped<MobileBookingCompanions_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new MobileBookingCompanions_db(connectionString!);
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<PermitStatus_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new PermitStatus_db(connectionString!);
});
builder.Services.AddScoped<Trophies_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new Trophies_db(connectionString!);
});
builder.Services.AddScoped<ITrophiesRepository>(sp =>
    sp.GetRequiredService<Trophies_db>()
);
builder.Services.AddScoped<PublicTrophies_db>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new PublicTrophies_db(connectionString!);
});
builder.Services.AddScoped<IPublicTrophiesRepository>(sp =>
    sp.GetRequiredService<PublicTrophies_db>()
);
builder.Services.AddScoped<ILegacyTrophyAuditRepository>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    return new LegacyTrophyAuditRepository(connectionString!);
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName)
);

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt configuration is missing.");
// The repo's appsettings secret is public: outside Development a private Jwt__SecretKey is required.
if (!builder.Environment.IsDevelopment() &&
    (jwtOptions.SecretKey.Length < 32 || jwtOptions.SecretKey.StartsWith("CHANGE-THIS", StringComparison.Ordinal)))
    throw new InvalidOperationException("Jwt:SecretKey is the public placeholder or too short. Set Jwt__SecretKey to a private 32+ character secret.");

builder.Services.AddScoped<Auth_db>();
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SecretKey)
            ),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        // A suspended user is refused on every request (checked at most every 15 s per user),
        // so the app signs out without waiting for the token to expire.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(id, out var userId)) return;
                var status = context.HttpContext.RequestServices.GetRequiredService<AccountStatusCheck>();
                if (await status.IsSuspendedAsync(userId))
                    context.Fail("Account suspended.");
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
        InternalServiceAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddSingleton(sp => new AccountStatusCheck(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!,
    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
builder.Services.AddHealthChecks();

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

// The app's sign-in had no lockout at all (the website's lockout lives in WebAPI), so it was the
// easy way to guess passwords: lock an address after 5 failed sign-ins and cap requests per IP.
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var rules = new AuthThrottleRules
    {
        MaxFailures = config.GetValue("AuthThrottle:MaxFailedSignIns", 5),
        FailureWindow = TimeSpan.FromMinutes(config.GetValue("AuthThrottle:LockoutMinutes", 15))
    };
    int perIp = config.GetValue("AuthThrottle:PerIpPerMinute", 30);
    rules.PerIp["/api/auth/login"] = (perIp, TimeSpan.FromMinutes(1));
    rules.PerIp["/api/auth/refresh"] = (perIp, TimeSpan.FromMinutes(1));
    rules.FailureLockoutPaths.Add("/api/auth/login");
    return rules;
});

var app = builder.Build();

// Load and validate committed reference data at startup instead of failing on a user's first search.
_ = app.Services.GetRequiredService<IRowlandWardCatalogueService>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseMiddleware<AuthThrottleMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseMiddleware<MaintenanceModeMiddleware>();   // the website's admin flag Enable_Maintenance_Mode
app.UseAuthorization();

// In Docker the proxy terminates HTTPS and the container only speaks HTTP; redirect only when an HTTPS port is set.
if (!string.IsNullOrWhiteSpace(app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"] ?? app.Configuration["HTTPS_PORTS"]))
    app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

