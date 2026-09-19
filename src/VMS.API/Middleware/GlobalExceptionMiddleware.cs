using System.Text.Json;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.API.Middleware;

/// <summary>Maps the domain exceptions services throw onto HTTP responses; anything else is a logged 500 with no detail leaked.</summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, message) = ex switch
            {
                BadRequestException => (StatusCodes.Status400BadRequest, ex.Message),
                UnauthorizedException => (StatusCodes.Status401Unauthorized, ex.Message),
                ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message),
                NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflictException => (StatusCodes.Status409Conflict, ex.Message),
                AccountLockedException locked => (StatusCodes.Status429TooManyRequests, locked.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            if (ex is AccountLockedException l)
                context.Response.Headers.RetryAfter = Math.Max(1, (int)(l.LockedUntilUtc - DateTime.UtcNow).TotalSeconds).ToString();

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse.Fail(message), Json));
        }
    }
}
