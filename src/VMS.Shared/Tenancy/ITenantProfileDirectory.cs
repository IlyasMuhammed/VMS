namespace VMS.Shared.Tenancy;

/// <summary>The tenant's own identity, for anything a module renders that must show who issued it (an invoice's
/// own company header, §34) — the same "look another module's data up through a small interface, never its
/// tables directly" pattern as <see cref="ITenantDirectory"/>, <c>IPartnerDirectory</c>, <c>IVehicleDirectory</c>.</summary>
public interface ITenantProfileDirectory
{
    Task<TenantProfile?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record TenantProfile(Guid Id, string Name, string? Address, string? ContactEmail, string? ContactPhone);
