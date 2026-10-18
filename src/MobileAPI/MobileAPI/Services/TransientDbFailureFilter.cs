using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using MobileApi.Database;

namespace MobileApi.Services;

/// <summary>
/// A database that is briefly blocked or unreachable used to surface as an unhandled 500 on
/// sign-in and token refresh. This answers it with 503 + Retry-After instead, so the app can tell
/// "try again" apart from "your session is invalid" (a 503 also never counts toward the
/// sign-in lockout, which only counts 401s).
/// </summary>
public sealed class TransientDbFailureFilterAttribute : ExceptionFilterAttribute
{
    public const int RetryAfterSeconds = 5;
    public const string Message = "Die bediener is tans besig. Probeer asseblief oor ’n paar sekondes weer.";

    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not SqlException exception || !Auth_db.IsTransient(exception))
            return;

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<TransientDbFailureFilterAttribute>>();
        logger.LogWarning(exception, "Transient SQL error {Number} on {Path}; answered 503",
            exception.Number, context.HttpContext.Request.Path);

        context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
        context.Result = new ObjectResult(new { success = false, message = Message })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
        context.ExceptionHandled = true;
    }
}
