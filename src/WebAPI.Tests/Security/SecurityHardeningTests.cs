using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Services;
using Xunit;

namespace WebAPI.Tests.Security;

/// <summary>Throttling of the anonymous account endpoints (AuthThrottleMiddleware).</summary>
public class AuthThrottleMiddlewareTests
{
    private static AuthThrottleMiddleware Build(AuthThrottleRules rules, int status = 200, Action<HttpContext>? onNext = null) =>
        new(ctx => { onNext?.Invoke(ctx); ctx.Response.StatusCode = status; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()), rules);

    private static DefaultHttpContext Request(string path, string? jsonBody = null, string ip = "203.0.113.7", string? query = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = jsonBody == null ? "GET" : "POST";
        ctx.Request.Path = path;
        if (query != null) ctx.Request.QueryString = new QueryString(query);
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Response.Body = new MemoryStream();
        if (jsonBody != null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.ContentType = "application/json";
        }
        return ctx;
    }

    private static async Task<int> Send(AuthThrottleMiddleware mw, DefaultHttpContext ctx)
    {
        await mw.InvokeAsync(ctx);
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task PerIpLimit_RefusesTheRequestAfterTheLimit()
    {
        var rules = new AuthThrottleRules();
        rules.PerIp["/api/user/register"] = (3, TimeSpan.FromMinutes(1));
        var mw = Build(rules);

        var codes = new List<int>();
        for (int i = 0; i < 4; i++) codes.Add(await Send(mw, Request("/api/user/register", "{}")));

        Assert.Equal(new[] { 200, 200, 200, 429 }, codes);
    }

    [Fact]
    public async Task PerIpLimit_CountsEachClientSeparately()
    {
        var rules = new AuthThrottleRules();
        rules.PerIp["/api/user/register"] = (1, TimeSpan.FromMinutes(1));
        var mw = Build(rules);

        Assert.Equal(200, await Send(mw, Request("/api/user/register", "{}", ip: "203.0.113.1")));
        Assert.Equal(200, await Send(mw, Request("/api/user/register", "{}", ip: "203.0.113.2")));
        Assert.Equal(429, await Send(mw, Request("/api/user/register", "{}", ip: "203.0.113.1")));
    }

    [Fact]
    public async Task PerEmailLimit_StopsMailBombingOneAddress_IgnoringCase()
    {
        var rules = new AuthThrottleRules();
        rules.PerEmail["/api/user/forgotPassword"] = (2, TimeSpan.FromMinutes(15));
        var mw = Build(rules);

        Assert.Equal(200, await Send(mw, Request("/api/user/forgotPassword", "{\"email\":\"jan@example.com\"}")));
        Assert.Equal(200, await Send(mw, Request("/api/user/forgotPassword", "{\"Email\":\"JAN@example.com\"}")));
        Assert.Equal(429, await Send(mw, Request("/api/user/forgotPassword", "{\"email\":\" jan@EXAMPLE.com \"}")));
        Assert.Equal(200, await Send(mw, Request("/api/user/forgotPassword", "{\"email\":\"other@example.com\"}")));
    }

    [Fact]
    public async Task PerEmailLimit_ReadsTheAddressFromTheQueryString()
    {
        var rules = new AuthThrottleRules();
        rules.PerEmail["/api/user/verifyEmail"] = (1, TimeSpan.FromMinutes(15));
        var mw = Build(rules);

        Assert.Equal(200, await Send(mw, Request("/api/user/verifyEmail", query: "?email=a@b.co&token=1")));
        Assert.Equal(429, await Send(mw, Request("/api/user/verifyEmail", query: "?email=A@B.co&token=2")));
    }

    [Fact]
    public async Task BodyIsStillReadableByTheControllerAfterThePeek()
    {
        var rules = new AuthThrottleRules();
        rules.PerEmail["/api/user/forgotPassword"] = (5, TimeSpan.FromMinutes(15));
        string? seen = null;
        var mw = Build(rules, onNext: ctx => seen = new StreamReader(ctx.Request.Body).ReadToEnd());

        await Send(mw, Request("/api/user/forgotPassword", "{\"email\":\"jan@example.com\"}"));

        Assert.Equal("{\"email\":\"jan@example.com\"}", seen);
    }

    [Fact]
    public async Task FailureLockout_LocksAnAddressAfterRepeatedFailedSignIns()
    {
        var rules = new AuthThrottleRules { MaxFailures = 3, FailureWindow = TimeSpan.FromMinutes(15) };
        rules.FailureLockoutPaths.Add("/api/auth/login");
        var mw = Build(rules, status: 401);

        var codes = new List<int>();
        for (int i = 0; i < 4; i++) codes.Add(await Send(mw, Request("/api/auth/login", "{\"email\":\"jan@example.com\",\"password\":\"x\"}")));

        Assert.Equal(new[] { 401, 401, 401, 429 }, codes);
    }

    [Fact]
    public async Task FailureLockout_ASuccessfulSignInResetsTheCount()
    {
        var rules = new AuthThrottleRules { MaxFailures = 2 };
        rules.FailureLockoutPaths.Add("/api/auth/login");
        var cache = new MemoryCache(new MemoryCacheOptions());
        int status = 401;
        var mw = new AuthThrottleMiddleware(ctx => { ctx.Response.StatusCode = status; return Task.CompletedTask; }, cache, rules);
        const string body = "{\"email\":\"jan@example.com\",\"password\":\"x\"}";

        await Send(mw, Request("/api/auth/login", body));      // 1 failure
        status = 200;
        await Send(mw, Request("/api/auth/login", body));      // success clears it
        status = 401;
        Assert.Equal(401, await Send(mw, Request("/api/auth/login", body)));
        Assert.Equal(401, await Send(mw, Request("/api/auth/login", body)));
        Assert.Equal(429, await Send(mw, Request("/api/auth/login", body)));
    }

    [Theory]
    [InlineData(15, "Probeer asseblief weer oor 15 minute.")]
    [InlineData(1, "Probeer asseblief weer oor 1 minuut.")]
    public async Task TheRefusalSaysHowLongToWait(int windowMinutes, string expected)
    {
        var rules = new AuthThrottleRules();
        rules.PerEmail["/api/user/forgotPassword"] = (1, TimeSpan.FromMinutes(windowMinutes));
        var mw = Build(rules);
        await Send(mw, Request("/api/user/forgotPassword", "{\"email\":\"jan@example.com\"}"));

        var ctx = Request("/api/user/forgotPassword", "{\"email\":\"jan@example.com\"}");
        await mw.InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();

        Assert.Equal(429, ctx.Response.StatusCode);
        Assert.Contains(expected, body);
        Assert.Equal((windowMinutes * 60).ToString(), ctx.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task OtherPathsAreNeverThrottled()
    {
        var rules = new AuthThrottleRules();
        rules.PerIp["/api/user/signin"] = (1, TimeSpan.FromMinutes(1));
        var mw = Build(rules);

        for (int i = 0; i < 5; i++)
            Assert.Equal(200, await Send(mw, Request("/api/packages", "{}")));
    }
}

/// <summary>500 responses must not hand exception/SQL details to the caller outside Development.</summary>
public class HideServerErrorDetailsFilterTests
{
    private sealed class Env : IWebHostEnvironment
    {
        public Env(string name) => EnvironmentName = name;
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "WebAPI";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; }
    }

    private static ResultExecutingContext Context(IActionResult result)
    {
        var action = new ActionContext(new DefaultHttpContext { TraceIdentifier = "trace-1" }, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(action, new List<IFilterMetadata>(), result, controller: new object());
    }

    private static string Json(object? value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public void Production_ReplacesA500BodyWithAGenericMessageAndTraceId()
    {
        var filter = new HideServerErrorDetailsFilter(new Env("Production"), NullLogger<HideServerErrorDetailsFilter>.Instance);
        var ctx = Context(new ObjectResult(new { message = "Invalid object name 'dbo.users2'.", details = "at SqlCommand..." }) { StatusCode = 500 });

        filter.OnResultExecuting(ctx);

        var body = Json(((ObjectResult)ctx.Result).Value);
        Assert.Equal(500, ((ObjectResult)ctx.Result).StatusCode);
        Assert.DoesNotContain("dbo.users2", body);
        Assert.DoesNotContain("SqlCommand", body);
        Assert.Contains("trace-1", body);
    }

    [Fact]
    public void Development_KeepsTheDetailsForDebugging()
    {
        var filter = new HideServerErrorDetailsFilter(new Env("Development"), NullLogger<HideServerErrorDetailsFilter>.Instance);
        var ctx = Context(new ObjectResult(new { message = "Invalid object name 'dbo.users2'." }) { StatusCode = 500 });

        filter.OnResultExecuting(ctx);

        Assert.Contains("dbo.users2", Json(((ObjectResult)ctx.Result).Value));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public void OtherStatusCodesAreLeftAlone(int status)
    {
        var filter = new HideServerErrorDetailsFilter(new Env("Production"), NullLogger<HideServerErrorDetailsFilter>.Instance);
        var ctx = Context(new ObjectResult(new { message = "Pakket nie gevind nie." }) { StatusCode = status });

        filter.OnResultExecuting(ctx);

        Assert.Contains("Pakket nie gevind nie.", Json(((ObjectResult)ctx.Result).Value));
    }
}

/// <summary>The startup migrations: every script is embedded, listed once and safe to run on any database.</summary>
public class DatabaseBootstrapperTests
{
    private static readonly Assembly WebApi = typeof(DatabaseBootstrapper).Assembly;

    [Fact]
    public void EveryListedScriptIsEmbedded()
    {
        var embedded = WebApi.GetManifestResourceNames();
        foreach (var script in DatabaseBootstrapper.MigrationScripts)
            Assert.Contains(script, embedded);
    }

    [Fact]
    public void EveryEmbeddedMigrationIsListed_SoNoneIsSilentlyNeverRun()
    {
        var listed = DatabaseBootstrapper.MigrationScripts.ToHashSet();
        var unlisted = WebApi.GetManifestResourceNames()
            .Where(n => n.StartsWith("db.") && n.EndsWith(".sql") && n != "db.Complete_create.sql" && !listed.Contains(n))
            .ToList();
        Assert.Empty(unlisted);
    }

    [Fact]
    public void ScriptsAreListedOnlyOnce()
    {
        var dupes = DatabaseBootstrapper.MigrationScripts.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(dupes);
    }

    [Fact]
    public void NoMigrationSwitchesDatabase_SoItRunsAgainstWhateverTheAppIsConfiguredFor()
    {
        foreach (var script in DatabaseBootstrapper.MigrationScripts)
        {
            var sql = DatabaseBootstrapper.PrepareMigrationSql(DatabaseBootstrapper.ReadEmbeddedResource(script));
            Assert.DoesNotMatch(@"(?im)^\s*USE\s+\[?\w+\]?\s*;?\s*$", sql);
        }
    }

    [Fact]
    public void PrepareMigrationSql_DropsOnlyTheUseLine()
    {
        var sql = "USE Mikpunt;\r\nGO\r\nSELECT 'USE Mikpunt;' AS x; -- USE in text stays\r\n  use [Other]\nGO";
        var prepared = DatabaseBootstrapper.PrepareMigrationSql(sql);
        Assert.Contains("SELECT 'USE Mikpunt;' AS x;", prepared);
        Assert.DoesNotMatch(@"(?im)^\s*USE\s", prepared);
    }

    [Fact]
    public void Checksum_IgnoresLineEndingsAndBom_ButNotContent()
    {
        var lf = "SELECT 1;\nGO\n";
        Assert.Equal(DatabaseBootstrapper.Checksum(lf), DatabaseBootstrapper.Checksum("﻿SELECT 1;\r\nGO\r\n"));
        Assert.NotEqual(DatabaseBootstrapper.Checksum(lf), DatabaseBootstrapper.Checksum("SELECT 2;\nGO\n"));
        Assert.Matches("^[0-9a-f]{64}$", DatabaseBootstrapper.Checksum(lf));
    }
}

/// <summary>Where a payment may send the browser back to (no open redirects).</summary>
public class WebAppOriginsTests
{
    private const string Base = "http://webapp.mikpunt.ddns.net";
    private const string Extra = "http://10.0.0.51:8080, https://webapp.mikpunt.ddns.net";

    [Theory]
    [InlineData("http://webapp.mikpunt.ddns.net/payment?bookingId=5")]
    [InlineData("https://webapp.mikpunt.ddns.net/payment")]
    [InlineData("http://10.0.0.51:8080/payment")]
    [InlineData("http://localhost:5180/payment")]
    public void OwnOriginsAreAllowed(string url) => Assert.True(WebAppOrigins.IsAllowedUrl(url, Base, Extra));

    [Theory]
    [InlineData("https://evil.example/payment")]
    [InlineData("http://webapp.mikpunt.ddns.net.evil.example/payment")]
    [InlineData("http://webapp.mikpunt.ddns.net@evil.example/payment")]
    [InlineData("http://10.0.0.51:9999/payment")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/payment")]
    [InlineData("/betaling")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsRefused(string? url) => Assert.False(WebAppOrigins.IsAllowedUrl(url, Base, Extra));
}

/// <summary>E-mail verification codes can't be brute-forced.</summary>
public class VerificationCodeStoreTests
{
    [Fact]
    public async Task TheIssuedCodeIsValid_CaseInsensitiveAddress()
    {
        var store = new InMemoryVerificationCodeStore();
        var code = await store.IssueAsync("Jan@Example.com");

        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(VerificationCodeResult.Valid, await store.CheckAsync("jan@example.com", code));
    }

    [Fact]
    public async Task AWrongCodeIsInvalid_AndAnUnknownAddressIsNotFound()
    {
        var store = new InMemoryVerificationCodeStore();
        var code = await store.IssueAsync("jan@example.com");
        var wrong = code == "123456" ? "654321" : "123456";

        Assert.Equal(VerificationCodeResult.Invalid, await store.CheckAsync("jan@example.com", wrong));
        Assert.Equal(VerificationCodeResult.NotFound, await store.CheckAsync("piet@example.com", code));
    }

    [Fact]
    public async Task AfterFiveWrongGuessesEvenTheRightCodeIsRefused()
    {
        var store = new InMemoryVerificationCodeStore();
        var code = await store.IssueAsync("jan@example.com");
        var wrong = code == "123456" ? "654321" : "123456";

        for (int i = 0; i < IVerificationCodeStore.MaxFailedAttempts; i++)
            await store.CheckAsync("jan@example.com", wrong);

        Assert.Equal(VerificationCodeResult.Locked, await store.CheckAsync("jan@example.com", code));
    }

    [Fact]
    public async Task ANewCodeReplacesTheOldOne_AndUnlocks()
    {
        var store = new InMemoryVerificationCodeStore();
        var first = await store.IssueAsync("jan@example.com");
        for (int i = 0; i < IVerificationCodeStore.MaxFailedAttempts; i++)
            await store.CheckAsync("jan@example.com", "000000");

        var second = await store.IssueAsync("jan@example.com");

        Assert.Equal(VerificationCodeResult.Valid, await store.CheckAsync("jan@example.com", second));
        if (first != second)
            Assert.Equal(VerificationCodeResult.Invalid, await store.CheckAsync("jan@example.com", first));
    }
}

/// <summary>Admin flag Enable_Maintenance_Mode: everyone but admins gets a 503; sign-in stays open.</summary>
public class MaintenanceModeMiddlewareTests
{
    private sealed class Flags : IFeatureFlags
    {
        private readonly bool _maintenance;
        public Flags(bool maintenance) => _maintenance = maintenance;
        public Task<bool> IsEnabledAsync(string key, bool fallback) => Task.FromResult(key == FeatureFlagKeys.MaintenanceMode ? _maintenance : fallback);
        public void Invalidate() { }
    }

    private static async Task<int> Send(bool maintenance, string path, string? role = null)
    {
        bool reached = false;
        var mw = new MaintenanceModeMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (role != null)
            ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role) }, "test"));
        await mw.InvokeAsync(ctx, new Flags(maintenance));
        return reached ? 200 : ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Off_EverythingPasses() => Assert.Equal(200, await Send(false, "/api/packets/getPackets"));

    [Theory]
    [InlineData("/api/packets/getPackets", null)]
    [InlineData("/api/bookings", "Jagter")]
    [InlineData("/api/user/register", null)]
    public async Task On_EveryoneButAdminsGets503(string path, string? role) => Assert.Equal(503, await Send(true, path, role));

    [Theory]
    [InlineData("/api/user/signin")]
    [InlineData("/api/settings/status")]
    [InlineData("/health")]
    public async Task On_SignInStatusAndHealthStayOpen(string path) => Assert.Equal(200, await Send(true, path));

    [Fact]
    public async Task On_AdminsCanKeepWorking() => Assert.Equal(200, await Send(true, "/api/bookings", "Admin"));
}

public class PayFastModeTests
{
    [Fact]
    public void TheSandboxFlagPicksPayFastsUrls()
    {
        var o = new WebAPI.Services.Payments.PayFastOptions { ProcessUrl = "https://configured/process" };
        Assert.Equal(WebAPI.Services.Payments.PayFastOptions.SandboxProcessUrl, o.ProcessUrlFor(true));
        Assert.Equal(WebAPI.Services.Payments.PayFastOptions.LiveProcessUrl, o.ProcessUrlFor(false));
        Assert.Equal("https://configured/process", o.ProcessUrlFor(null));
        Assert.Equal(WebAPI.Services.Payments.PayFastOptions.LiveValidateUrl, o.ValidateUrlFor(false));
    }
}
