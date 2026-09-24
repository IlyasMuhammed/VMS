namespace VMS.Modules.Core.Models;

public class LookupAttributeModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Flag, Number, Text or Lookup.</summary>
    public string Kind { get; set; } = string.Empty;
    public bool Required { get; set; }
    /// <summary>For a Lookup attribute: the lookup type whose values may be chosen.</summary>
    public string? LookupType { get; set; }
}

public class LookupTypeModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<LookupAttributeModel> Attributes { get; set; } = [];
    public int ActiveCount { get; set; }
    public int TotalCount { get; set; }
}

public class CreateLookupRequest
{
    /// <summary>Capital letters, digits and underscores. Cannot be changed later.</summary>
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Where it sits in the list. Left out, it goes to the end.</summary>
    public int? SortOrder { get; set; }
    public Dictionary<string, string?>? Attributes { get; set; }
}

public class UpdateLookupRequest
{
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string?>? Attributes { get; set; }
}
