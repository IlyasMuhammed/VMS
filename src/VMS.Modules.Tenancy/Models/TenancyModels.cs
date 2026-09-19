namespace VMS.Modules.Tenancy.Models;

public class TenantFilter
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class TenantListItemModel
{
    public Guid Id { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class TenantDetailModel : TenantListItemModel
{
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Version (content hash) of the logo for light mode, or null when none is uploaded.</summary>
    public string? LogoLightVersion { get; set; }
    /// <summary>Version (content hash) of the logo for dark mode, or null when none is uploaded.</summary>
    public string? LogoDarkVersion { get; set; }
}

/// <summary>A stored logo, ready to be streamed back.</summary>
public sealed record TenantLogoFile(byte[] Content, string ContentType, string Version);

public class CreateTenantRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }

    // The tenant's first admin — invited by email to set their own password.
    public string AdminFirstName { get; set; } = string.Empty;
    public string? AdminLastName { get; set; }
    public string AdminEmail { get; set; } = string.Empty;
}

public class CreateTenantResult
{
    public Guid TenantId { get; set; }
    public int AdminUserId { get; set; }
    /// <summary>
    /// The admin's invite link, returned so the Super Admin can hand it over directly when outbound
    /// email is not configured. Shown once — it is not stored in clear.
    /// </summary>
    public string AdminInviteLink { get; set; } = string.Empty;
}

public class UpdateTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
}

public class PatchTenantStatusRequest
{
    public bool IsActive { get; set; }
}
