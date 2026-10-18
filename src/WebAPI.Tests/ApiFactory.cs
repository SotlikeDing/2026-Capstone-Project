using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace WebAPI.Tests;

/// <summary>
/// The WebAPI under test, for tests that read JSON response bodies. The test project rolls
/// forward to the newest installed runtime (RollForward=LatestMajor), where System.Text.Json
/// needs PipeWriter.UnflushedBytes - which the .NET 8 TestServer's response writer lacks, so
/// every JSON response became a 500. This swaps in a plain stream-backed response body.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services => services.AddTransient<IStartupFilter, StreamResponseBodyFilter>());

    private sealed class StreamResponseBodyFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var original = context.Features.Get<IHttpResponseBodyFeature>()!;
                var streamed = new StreamResponseBodyFeature(original.Stream, original);
                context.Features.Set<IHttpResponseBodyFeature>(streamed);
                try
                {
                    await nextMiddleware(context);
                    await streamed.CompleteAsync();
                }
                finally
                {
                    context.Features.Set(original);
                }
            });
            next(app);
        };
    }
}
