using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebAPI.Services;

/// <summary>
/// Many controllers answer a caught exception with StatusCode(500, new { message = ex.Message, ... }),
/// which hands SQL/table details to any caller. Outside Development this swaps every 500 body for a
/// generic message plus the trace id, and writes the original body to the log under that trace id.
/// </summary>
public sealed class HideServerErrorDetailsFilter : IAlwaysRunResultFilter
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HideServerErrorDetailsFilter> _logger;

    public HideServerErrorDetailsFilter(IWebHostEnvironment env, ILogger<HideServerErrorDetailsFilter> logger)
    {
        _env = env;
        _logger = logger;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (_env.IsDevelopment() || context.Result is not ObjectResult { StatusCode: 500 } result)
            return;

        string traceId = context.HttpContext.TraceIdentifier;
        string original;
        try { original = JsonSerializer.Serialize(result.Value); }
        catch (Exception) { original = result.Value?.ToString() ?? ""; }
        _logger.LogError("500 from {Path} (trace {TraceId}): {Body}", context.HttpContext.Request.Path, traceId, original);

        context.Result = new ObjectResult(new
        {
            success = false,
            message = "Iets het op die bediener fout gegaan. Probeer asseblief weer.",
            traceId
        }) { StatusCode = 500 };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
