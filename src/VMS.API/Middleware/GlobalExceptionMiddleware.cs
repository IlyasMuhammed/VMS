using System.Text.Json;
using VMS.Shared.Auditing;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.API.Middleware;

/// <summary>Maps the domain exceptions services throw onto HTTP responses; anything else is a logged 500 with no detail leaked.</summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The same id every audit row from this request carries, so a report can be matched back to the trail.</summary>
    private static string? CorrelationId(HttpContext context) =>
        context.RequestServices.GetService<IAuditContext>()?.GroupId.ToString();

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException invalid) when (!context.Response.HasStarted)
        {
            // Every problem at once, each tied to its field, so the screen can mark them all.
            var response = new ApiResponse
            {
                Success = false,
                Message = invalid.Message,
                Errors = invalid.Errors.Select(e => new ApiError { Field = e.Field, Code = e.Code, Message = e.Message, Params = e.Params }).ToList(),
                CorrelationId = CorrelationId(context)
            };
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, Json));
        }
        catch (BusinessRuleException rule) when (!context.Response.HasStarted)
        {
            // A business rule refused the request (a missing rate, an overlap, a wrong status) — not a form problem,
            // so it is answered as 422 with a machine-readable code rather than folded into the 400 validation shape.
            var response = new ApiResponse
            {
                Success = false,
                Message = rule.Message,
                Code = rule.Code,
                CorrelationId = CorrelationId(context),
                Errors = rule.Details?.Select(d => new ApiError { Field = d.Field, Code = rule.Code, Message = d.Reason }).ToList()
            };
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, Json));
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, message, code) = ex switch
            {
                BadRequestException => (StatusCodes.Status400BadRequest, ex.Message, (string?)null),
                UnauthorizedException => (StatusCodes.Status401Unauthorized, ex.Message, null),
                ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message, "FORBIDDEN"),
                NotFoundException => (StatusCodes.Status404NotFound, ex.Message, null),
                ConcurrencyConflictException => (StatusCodes.Status409Conflict, ex.Message, "CONCURRENCY_CONFLICT"),
                ConflictException => (StatusCodes.Status409Conflict, ex.Message, null),
                AccountLockedException locked => (StatusCodes.Status429TooManyRequests, locked.Message, null),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null)
            };

            if (status == StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            if (ex is AccountLockedException l)
                context.Response.Headers.RetryAfter = Math.Max(1, (int)(l.LockedUntilUtc - DateTime.UtcNow).TotalSeconds).ToString();

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            var response = ApiResponse.Fail(message);
            response.Code = code;
            response.CorrelationId = CorrelationId(context);
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, Json));
        }
    }
}
