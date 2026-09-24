using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VMS.Shared.Auditing;
using VMS.Shared.Pagination;

namespace VMS.Shared.Authorization;

/// <summary>One refused request: who, what they needed, where they tried it (BR-SEC-007).</summary>
public sealed record AccessDenial(
    DateTime OccurredAt,
    Guid TenantId,
    int UserId,
    string? UserName,
    string Permission,
    string Method,
    string Path,
    string? RouteValues,
    string? IpAddress);

/// <summary>Where refused requests are recorded. Implemented by the module that owns the table.</summary>
public interface IAccessDenialSink
{
    Task RecordAsync(AccessDenial denial);
}

/// <summary>
/// Keeps ASP.NET's own 401 and 403 behaviour, and on a 403 also writes the denial to the log and to
/// the <see cref="IAccessDenialSink"/>. Recording is best effort: a failure to record must never turn a
/// clean 403 into a 500 or let the request through.
/// </summary>
public sealed class DenialLoggingAuthorizationResultHandler(ILogger<DenialLoggingAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            await RecordAsync(context, authorizeResult);
            await WriteForbiddenBodyAsync(context);
            return; // fully handled: skip ASP.NET's own (bodyless) ForbidResult
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// The same JSON shape every other refusal in this API answers with, so a 403 is not the one response a
    /// caller gets an empty body for. Every existing test checks only the status code, never the body, so this
    /// is additive.
    /// </summary>
    private static async Task WriteForbiddenBodyAsync(HttpContext context)
    {
        if (context.Response.HasStarted) return;
        var response = ApiResponse.Fail("You do not have permission to perform this action.");
        response.Code = "FORBIDDEN";
        response.CorrelationId = context.RequestServices.GetService<IAuditContext>()?.GroupId.ToString();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, Json));
    }

    private async Task RecordAsync(HttpContext context, PolicyAuthorizationResult result)
    {
        try
        {
            var failed = result.AuthorizationFailure?.FailedRequirements ?? [];
            var permission = failed.OfType<PermissionRequirement>().FirstOrDefault()?.Permission
                ?? (failed.OfType<SuperAdminRequirement>().Any() ? "SUPER_ADMIN" : "unknown");

            var route = context.Request.RouteValues
                .Where(kv => kv.Key is not ("controller" or "action") && kv.Value is not null)
                .Select(kv => $"{kv.Key}={kv.Value}");
            var routeValues = string.Join("&", route);

            var denial = new AccessDenial(
                DateTime.UtcNow,
                context.User.GetTenantId(),
                context.User.GetUserId(),
                context.User.FindFirst("user_name")?.Value,
                permission,
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                routeValues.Length == 0 ? null : routeValues,
                context.Connection.RemoteIpAddress?.ToString());

            logger.LogWarning(
                "Access denied: user {UserId} lacks {Permission} for {Method} {Path} {RouteValues}",
                denial.UserId, denial.Permission, denial.Method, denial.Path, denial.RouteValues);

            var sink = context.RequestServices.GetService<IAccessDenialSink>();
            if (sink is not null) await sink.RecordAsync(denial);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record an access denial.");
        }
    }
}
