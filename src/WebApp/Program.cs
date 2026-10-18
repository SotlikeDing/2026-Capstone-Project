using System.Globalization;
using WebApp.Components;
using WebApp.Services;

// The whole UI is Afrikaans - without this, ToString("MMM")/("MMMM") date formats
// throughout the app render English month names under the server's default culture.
var afrikaansCulture = new CultureInfo("af-ZA");
CultureInfo.DefaultThreadCurrentCulture = afrikaansCulture;
CultureInfo.DefaultThreadCurrentUICulture = afrikaansCulture;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32MB
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });


builder.Services.AddHttpContextAccessor();

// Register HttpClient with base address pointing to WebAPI. AuthTokenHandler attaches the
// circuit's JWT to every request, since most WebAPI endpoints now require authentication.
builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["ApiBaseUrl"] ?? "http://localhost:5004/";
    return new HttpClient(new AuthTokenHandler(sp.GetRequiredService<AuthService>(), sp.GetRequiredService<ILogger<AuthTokenHandler>>(),
        sp.GetRequiredService<IHttpContextAccessor>())) { BaseAddress = new Uri(baseUrl) };
});

// Farm photos are public: the browser asks the website for /plaasfoto/{farmId}/{index} and the
// website fetches them from the WebAPI without a user token (see the MapGet below).
builder.Services.AddHttpClient("WebApiPublic", (sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["ApiBaseUrl"] ?? "http://localhost:5004/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register MikPunt Platform custom services (Scoped per Blazor circuit)
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MikpuntApiService>();
builder.Services.AddScoped<Saps520PdfService>();
builder.Services.AddScoped<AiDraftHandoff>();

// AI Helper client: own HttpClient because an LLM answer can take a minute or more (the shared client above times out at 100s).
builder.Services.AddScoped(sp =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["ApiBaseUrl"] ?? "http://localhost:5004/";
    return new AiHelperClient(
        new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(200) },
        sp.GetRequiredService<AuthService>());
});

// Behind the proxy the socket address is the proxy's; trust its X-Forwarded-For (private networks only)
// so the visitor's real address can be passed on to the WebAPI (see AuthTokenHandler).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fout", createScopeForErrors: true);
    app.UseHsts();
}

// Security headers. Inline scripts in App.razor and the Bootstrap Icons/Google Fonts CDNs are allowed;
// framing, plugins, <base> hijacking and form posts anywhere but here and PayFast are not.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), payment=(), usb=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' data: https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
        "img-src 'self' data: blob: https: http:; " +
        "media-src 'self' blob: data: https: http:; " +
        "connect-src 'self' ws: wss: data: blob:; " +
        "frame-src 'self' blob: https://www.youtube.com https://www.youtube-nocookie.com; " +
        "frame-ancestors 'none'; object-src 'none'; base-uri 'self'; " +
        "form-action 'self' https://sandbox.payfast.co.za https://www.payfast.co.za";
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/plaasfoto/{farmId:int}/{index:int}", async (int farmId, int index, IHttpClientFactory factory, HttpContext context) =>
{
    using var response = await factory.CreateClient("WebApiPublic").GetAsync($"api/farmer/{farmId}/images/{index}", context.RequestAborted);
    if (!response.IsSuccessStatusCode) return Results.NotFound();
    var bytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
    // The ?v= in the path changes whenever the farm's photos change, so a long cache is safe.
    context.Response.Headers.CacheControl = "public, max-age=604800";
    return Results.File(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
