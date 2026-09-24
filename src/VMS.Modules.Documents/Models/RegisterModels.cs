namespace VMS.Modules.Documents.Models;

/// <summary>Filters for the Document Register (§23A.4). <b>Left for later:</b> branch, vehicle category and partner-role filters, which need richer
/// cross-module lookups than <c>IVehicleDirectory</c>/<c>IPartnerDirectory</c> carry today — the register still covers the compliance officer's
/// main question (what is expiring, what is missing) without them.</summary>
public class RegisterQuery
{
    public string? OwnerType { get; set; }
    public int? DocumentTypeId { get; set; }
    public string? Status { get; set; }
    public int? ExpiringWithinDays { get; set; }
}

public class RegisterRow
{
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
}

/// <summary>One owner missing one mandatory or periodic document (the Missing Documents report, §23A.4).</summary>
public class MissingDocumentRow
{
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public bool Required { get; set; }
}

/// <summary>One document due to expire within the requested month (the Expiry Calendar, §23A.4).</summary>
public class CalendarEntry
{
    public DateOnly Date { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public int DocumentId { get; set; }
}
