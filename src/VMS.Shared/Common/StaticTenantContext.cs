namespace VMS.Shared.Common;

/// <summary>Settable <see cref="ITenantContext"/> for design-time tooling and tests.</summary>
public sealed class StaticTenantContext : ITenantContext
{
    public Guid TenantId { get; set; } = TenantDefaults.PlatformTenantId;
    public bool IsSuperAdmin { get; set; } = true;
}
