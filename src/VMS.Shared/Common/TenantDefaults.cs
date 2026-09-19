namespace VMS.Shared.Common;

public static class TenantDefaults
{
    /// <summary>
    /// The fixed id of the platform's own tenant — the one the Super Admin belongs to. It must be a
    /// stable constant (not a runtime Guid) so the seeder and every environment agree on it.
    /// </summary>
    public static readonly Guid PlatformTenantId = Guid.Parse("7A1C5E10-0000-4000-8000-000000000001");

    public const string PlatformTenantCode = "VMS-PLATFORM";
}
