using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Trips.Services;

/// <summary>What was stored (or found) for one Idempotency-Key + route.</summary>
internal sealed record IdempotencyReplay(int StatusCode, string Body, string? ContentType);

/// <summary>
/// Backs the <c>Idempotency-Key</c> convention (FSD §47.1: a replay returns the first response). Scoped to this
/// module's own schema (CC-00's decision) — promote to <c>VMS.Shared</c> only if another module needs it too.
/// </summary>
internal interface IIdempotencyStore
{
    Task<IdempotencyReplay?> FindAsync(string key, string route, string requestHash, CancellationToken ct);
    Task<IdempotencyReplay> SaveAsync(string key, string route, string requestHash, int statusCode, string body, string? contentType, CancellationToken ct);
}

internal sealed class IdempotencyStore(TripsDbContext db, ITenantContext tenant) : IIdempotencyStore
{
    public async Task<IdempotencyReplay?> FindAsync(string key, string route, string requestHash, CancellationToken ct)
    {
        var found = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.Key == key && r.Route == route, ct);
        if (found is null) return null;

        if (!string.Equals(found.RequestHash, requestHash, StringComparison.Ordinal))
            throw new BusinessRuleException("IDEMPOTENCY_KEY_REUSED",
                "This Idempotency-Key was already used for a request with different content.",
                [new BusinessRuleDetail("Idempotency-Key", key, "The key must be unique per distinct request, or reused only to replay the exact same request.")]);

        return new IdempotencyReplay(found.ResponseStatusCode, found.ResponseBody, found.ResponseContentType);
    }

    public async Task<IdempotencyReplay> SaveAsync(string key, string route, string requestHash, int statusCode, string body, string? contentType, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key, Route = route, RequestHash = requestHash,
            ResponseStatusCode = statusCode, ResponseBody = body, ResponseContentType = contentType,
            CreatedOn = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return new IdempotencyReplay(statusCode, body, contentType);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The same race the Notifications module's own dedup index accepts (§ "known, accepted test-coverage
            // limit"): two requests with the same key arrived close enough together that both ran the action.
            // Neither can be un-run at this point; the best remaining guarantee is that every caller ends up
            // seeing the SAME stored response, whichever request's insert actually won.
            var winner = await db.IdempotencyRecords.AsNoTracking()
                .FirstAsync(r => r.TenantId == tenant.TenantId && r.Key == key && r.Route == route, ct);
            return new IdempotencyReplay(winner.ResponseStatusCode, winner.ResponseBody, winner.ResponseContentType);
        }
    }
}

/// <summary>
/// <c>[Idempotent]</c> on a POST that creates money or a ledger record. Requires the <c>Idempotency-Key</c>
/// header (VAL-GEN-021 if missing); a first call runs the action and remembers its result; a replay with the
/// same key and the same request returns that remembered result without running the action again.
/// <para>
/// Scope, honestly stated: this only captures results the action returns as an <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/>
/// (200/201/409/422/…). A thrown exception is handled by <c>GlobalExceptionMiddleware</c> further up the pipeline,
/// outside this filter's reach, so a request that fails with a thrown validation or business-rule exception is
/// not remembered — only a genuine, successfully-returned result is. That is the case that matters (never charge
/// or post twice); replaying a thrown-exception rejection identically is a nice-to-have this does not attempt.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var messages = context.HttpContext.RequestServices.GetRequiredService<VMS.Shared.Messages.IMessageCatalogue>();
        if (!context.HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) || string.IsNullOrWhiteSpace(keyValues.ToString()))
            throw new ValidationException(messages.Error(null, VMS.Shared.Messages.Msg.IdempotencyKeyRequired));

        var key = keyValues.ToString();
        var route = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        var requestHash = HashArguments(context.ActionArguments);

        var store = context.HttpContext.RequestServices.GetRequiredService<IIdempotencyStore>();
        var existing = await store.FindAsync(key, route, requestHash, context.HttpContext.RequestAborted);
        if (existing is not null)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ContentResult
            {
                StatusCode = existing.StatusCode, Content = existing.Body, ContentType = existing.ContentType ?? "application/json"
            };
            return;
        }

        var executed = await next();
        if (executed.Exception is not null || executed.Result is not Microsoft.AspNetCore.Mvc.ObjectResult objectResult) return;

        // Serialize with the app's own actual configured options (camelCase, null-omitting, the UTC date
        // converter, field-permission trimming, …), not a second, separately-hardcoded set of settings that
        // could silently drift from them — a real, latent mismatch this filter's own first production caller
        // (CC-29's Submit) exposed: the real first response and the filter's own replayed body disagreed on
        // whether a null property (e.g. "cancelledOn") is written at all.
        var jsonOptions = context.HttpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
        var body = JsonSerializer.Serialize(objectResult.Value, jsonOptions);
        await store.SaveAsync(key, route, requestHash, statusCode, body, "application/json", context.HttpContext.RequestAborted);
    }

    private static string HashArguments(IDictionary<string, object?> arguments)
    {
        var json = JsonSerializer.Serialize(arguments.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
