namespace VMS.Shared.Exceptions;

// Each maps to one HTTP status in GlobalExceptionMiddleware, so services throw these instead of
// building HTTP responses.

public class BadRequestException(string message) : Exception(message);          // 400
public class UnauthorizedException(string message) : Exception(message);        // 401
public class ForbiddenException(string message) : Exception(message);           // 403
public class NotFoundException(string message) : Exception(message);            // 404
public class ConflictException(string message) : Exception(message);            // 409

public class AccountLockedException(DateTime lockedUntilUtc)
    : Exception($"Account is locked. Try again after {lockedUntilUtc:u}.")       // 429
{
    public DateTime LockedUntilUtc { get; } = lockedUntilUtc;
}
