using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebAPI.Middleware
{
    /// <summary>
    /// Global catch-all: logs any unhandled exception and returns a JSON 500 instead
    /// of an ASP.NET Core default error page/stack trace HTML dump.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception has occurred: {Message}", ex.Message);
                await HandleExceptionAsync(httpContext, ex);
            }
        }

        /// <summary>
        /// Writes the JSON 500 response. Note: DetailedMessage exposes the raw
        /// exception.Message to the client on every unhandled error - fine for this
        /// capstone project's own debugging, but a real production API would
        /// typically omit exception details from client-facing responses and rely
        /// on the server-side log (written above in InvokeAsync) instead.
        /// </summary>
        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "Internal Server Error from the custom middleware.",
                DetailedMessage = exception.Message
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(response, options);

            return context.Response.WriteAsync(json);
        }
    }
}

