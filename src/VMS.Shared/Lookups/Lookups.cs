using Microsoft.Extensions.DependencyInjection;

namespace VMS.Shared.Lookups;

/// <summary>What kind of value an extra attribute of a lookup holds. All are stored as text and checked on save.</summary>
public enum LookupAttributeKind
{
    /// <summary>Yes or no. Stored as <c>true</c> / <c>false</c>; absent means false.</summary>
    Flag,
    /// <summary>A whole number, zero or more.</summary>
    Number,
    /// <summary>Short free text.</summary>
    Text,
    /// <summary>The code of a value in another lookup (a city's province). Must be an active value there.</summary>
    Lookup,
}

/// <param name="Key">Stable name, for example <c>expirable</c>.</param>
/// <param name="LookupType">For <see cref="LookupAttributeKind.Lookup"/>: the lookup type whose codes are allowed.</param>
public sealed record LookupAttribute(string Key, string Label, LookupAttributeKind Kind, bool Required = false, string? LookupType = null);

/// <summary>A value a tenant starts with. Its position in the list is its sort order.</summary>
public sealed record LookupSeed(string Code, string Description, IReadOnlyDictionary<string, string?>? Attributes = null);

/// <summary>
/// One kind of master list (Vehicle Type, Make, …). Defined in code, so a screen or a rule can rely on the
/// type existing; its values are data, edited by each tenant under Administration.
/// </summary>
/// <param name="Code">Stable identifier, upper case with underscores.</param>
/// <param name="Attributes">Extra fields each value carries, beyond code, description, sort order and active flag.</param>
/// <param name="Defaults">Values every tenant starts with, created the first time the list is used.</param>
public sealed record LookupTypeDefinition(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<LookupAttribute>? Attributes = null,
    IReadOnlyList<LookupSeed>? Defaults = null)
{
    public IReadOnlyList<LookupAttribute> Schema => Attributes ?? [];
    public IReadOnlyList<LookupSeed> Seeds => Defaults ?? [];
}

/// <summary>A value of a lookup, as shown in a picker or on the administration screen.</summary>
/// <param name="Id">The value's key. A record that uses a lookup value stores this (or the code) and validates it through <see cref="ILookupReader"/>.</param>
public sealed record LookupItem(
    int Id,
    string Code,
    string Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyDictionary<string, string?> Attributes);

/// <summary>Every lookup type the platform and its modules define.</summary>
public interface ILookupCatalog
{
    IReadOnlyList<LookupTypeDefinition> All { get; }

    LookupTypeDefinition? Find(string code);
}

/// <summary>What other modules use to fill dropdowns and to check that a chosen value is real, current and the tenant's own.</summary>
public interface ILookupReader
{
    /// <summary>The active values of a type for the signed-in tenant, in sort order. Inactive values are not offered for new records.</summary>
    /// <param name="attributeFilter">Only values whose extra fields have these values, for example <c>province = PUNJAB</c> for the cities of one province. A field the type does not define is refused.</param>
    Task<IReadOnlyList<LookupItem>> GetActiveAsync(string lookupType, IReadOnlyDictionary<string, string>? attributeFilter = null);

    /// <summary>A value of the type, active or not (a record made last year may still carry a value since retired).</summary>
    Task<LookupItem?> FindAsync(string lookupType, int id);

    /// <summary>Several values at once, active or not, keyed by id: for labelling a page of rows with one query. Ids that are not this tenant's are left out.</summary>
    Task<IReadOnlyDictionary<int, LookupItem>> FindManyAsync(string lookupType, IEnumerable<int> ids);

    /// <summary>True if the value exists in this tenant's list and can still be chosen.</summary>
    Task<bool> IsActiveAsync(string lookupType, int id);
}

public static class LookupServiceExtensions
{
    /// <summary>Registers a lookup type. A module calls this for the lists it owns; see <see cref="PlatformLookups"/>.</summary>
    public static IServiceCollection AddLookupType(this IServiceCollection services, LookupTypeDefinition definition)
    {
        services.AddSingleton(definition);
        return services;
    }
}
