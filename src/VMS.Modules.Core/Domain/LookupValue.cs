using VMS.Shared.Common;

namespace VMS.Modules.Core.Domain;

/// <summary>
/// One value of a tenant's master list. Never deleted: records that used it keep pointing at it, so it is
/// retired with <see cref="IsActive"/> instead, and stops being offered for new records.
/// </summary>
internal class LookupValue : ITenantScopedEntity
{
    public int LookupValueID { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Which list, for example <c>VEHICLE_TYPE</c>. See <c>PlatformLookups</c>.</summary>
    public string LookupType { get; set; } = string.Empty;
    /// <summary>Stable and unique within the list. Business rules may recognise it, so it never changes after creation.</summary>
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The list's extra fields as a JSON object of text values, for example <c>{"expirable":"true"}</c>.</summary>
    public string? Attributes { get; set; }
}
