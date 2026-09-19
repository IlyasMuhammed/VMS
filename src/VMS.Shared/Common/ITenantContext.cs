namespace VMS.Shared.Common;

/// <summary>
/// Resolves the current request's tenant. <see cref="IsSuperAdmin"/> bypasses every query filter
/// (see <see cref="TenantScopingExtensions"/>). Anonymous code paths — login, refresh,
/// forgot-password — and startup seeding have no authenticated user and therefore no tenant to
/// resolve, so they also report <c>IsSuperAdmin = true</c>; those paths only ever look rows up by a
/// specific unique key (email, token hash), never by a broad listing.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsSuperAdmin { get; }
}
