using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>
/// One remembered response to a POST that created money or a ledger record (FSD §47.1: "a replay returns the
/// first response"). Bookkeeping, not a business record — never shown to a user, so it is excluded from the
/// audit trail (<see cref="NotAuditedAttribute"/>) the same way sign-in counters and secrets are.
/// Kept indefinitely: this module's own retention rule is "no retention limit" (FSD §54), and one row per
/// distinct (tenant, key, route) is small.
/// </summary>
[NotAudited]
internal sealed class IdempotencyRecord : ITenantScopedEntity
{
    public long IdempotencyRecordId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>The client-supplied <c>Idempotency-Key</c> header value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>"POST /api/invoices/{id}/submit" — the same key on a different route is a different record.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>SHA-256 of the request body, so a key reused with a genuinely different payload can be told apart
    /// from a true replay instead of silently returning the wrong response.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public int ResponseStatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public string? ResponseContentType { get; set; }

    public DateTime CreatedOn { get; set; }
}
