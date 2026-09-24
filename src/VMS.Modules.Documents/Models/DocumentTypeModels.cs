namespace VMS.Modules.Documents.Models;

public class DocumentTypeModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>"BusinessPartner", "Vehicle", or both.</summary>
    public List<string> AppliesTo { get; set; } = [];
    public string? PartnerRole { get; set; }
    public bool IsExpirable { get; set; }
    public int? DefaultValidityValue { get; set; }
    public string? DefaultValidityUnit { get; set; }
    public bool IsPeriodic { get; set; }
    public int RenewalLeadDays { get; set; }
    public string MandatoryLevel { get; set; } = string.Empty;
    public bool RequiresDocumentNumber { get; set; }
    public bool HasCost { get; set; }
    public List<string> AllowedFormats { get; set; } = [];
    public int MaxFileSizeMb { get; set; }
    public int RetentionYears { get; set; }
    public bool IsActive { get; set; }
    /// <summary>BR-DOC-006: the Recurring Charge Type code this type's renewal can offer to link a payment to, where one exists (<see cref="Domain.DocumentChargeLink"/>).</summary>
    public string? LinkedChargeTypeCode { get; set; }
}

public class SaveDocumentTypeRequest
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public List<string>? AppliesTo { get; set; }
    public string? PartnerRole { get; set; }
    public bool IsExpirable { get; set; }
    public int? DefaultValidityValue { get; set; }
    public string? DefaultValidityUnit { get; set; }
    public bool IsPeriodic { get; set; }
    public int? RenewalLeadDays { get; set; }
    public string? MandatoryLevel { get; set; }
    public bool RequiresDocumentNumber { get; set; }
    public bool HasCost { get; set; }
    public List<string>? AllowedFormats { get; set; }
    public int? MaxFileSizeMb { get; set; }
    public int? RetentionYears { get; set; }
    public bool IsActive { get; set; } = true;
}
