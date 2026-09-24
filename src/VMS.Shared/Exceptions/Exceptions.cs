namespace VMS.Shared.Exceptions;

// Each maps to one HTTP status in GlobalExceptionMiddleware, so services throw these instead of
// building HTTP responses.

public class BadRequestException(string message) : Exception(message);          // 400
public class UnauthorizedException(string message) : Exception(message);        // 401
public class ForbiddenException(string message) : Exception(message);           // 403
public class NotFoundException(string message) : Exception(message);            // 404
public class ConflictException(string message) : Exception(message);            // 409

/// <summary>
/// The specific case of a 409 that is a stale row version (a save against data someone else already changed),
/// so the response can carry the FSD's <c>CONCURRENCY_CONFLICT</c> code without mislabelling every other
/// <see cref="ConflictException"/> (duplicates, wrong status, etc.) the same way.
/// </summary>
public class ConcurrencyConflictException(string message) : ConflictException(message);

public class AccountLockedException(DateTime lockedUntilUtc)
    : Exception($"Account is locked. Try again after {lockedUntilUtc:u}.")       // 429
{
    public DateTime LockedUntilUtc { get; } = lockedUntilUtc;
}

/// <summary>One extra fact about a business-rule failure, for <see cref="BusinessRuleException"/> — the FSD's own
/// error shape (§47.1): <c>{ field, entityId, reason }</c>.</summary>
public sealed record BusinessRuleDetail(string? Field, object? EntityId, string Reason);

/// <summary>
/// A business rule refused the request in a way that is not a form-validation problem (a missing rate, an
/// overlap, a wrong status) — answered as 422 with a machine-readable <see cref="Code"/>
/// (for example <c>RATE_MISSING</c>) and, optionally, which rows caused it. Introduced for the Trip/Billing/
/// Invoicing/Ledger module (FSD §47.1's error shape); any module may throw it.
/// </summary>
public class BusinessRuleException(string code, string message, IReadOnlyList<BusinessRuleDetail>? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<BusinessRuleDetail>? Details { get; } = details;
}
