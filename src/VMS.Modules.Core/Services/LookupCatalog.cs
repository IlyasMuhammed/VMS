using System.Text.RegularExpressions;
using VMS.Shared.Lookups;

namespace VMS.Modules.Core.Services;

/// <summary>
/// Every registered lookup type. Checked once, at start-up, so a mistake in a definition (two types with one
/// code, an attribute that points at a list that does not exist) stops the application rather than surfacing
/// as a confusing error on a screen.
/// </summary>
internal sealed partial class LookupCatalog : ILookupCatalog
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,39}$")]
    private static partial Regex TypeCode();

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,29}$")]
    private static partial Regex AttributeKey();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_]{0,29}$")]
    private static partial Regex ValueCode();

    private readonly Dictionary<string, LookupTypeDefinition> _byCode;

    public LookupCatalog(IEnumerable<LookupTypeDefinition> definitions)
    {
        All = definitions.ToList();
        var problems = new List<string>();

        _byCode = new Dictionary<string, LookupTypeDefinition>();
        foreach (var d in All)
        {
            if (!TypeCode().IsMatch(d.Code)) problems.Add($"'{d.Code}' is not a valid lookup type code.");
            if (!_byCode.TryAdd(d.Code, d)) problems.Add($"Lookup type '{d.Code}' is defined twice.");
        }

        foreach (var d in All)
        {
            var keys = new HashSet<string>();
            foreach (var a in d.Schema)
            {
                if (!AttributeKey().IsMatch(a.Key)) problems.Add($"{d.Code}: '{a.Key}' is not a valid attribute key.");
                if (!keys.Add(a.Key)) problems.Add($"{d.Code}: attribute '{a.Key}' is defined twice.");
                if (a.Kind == LookupAttributeKind.Lookup && (a.LookupType is null || !_byCode.ContainsKey(a.LookupType)))
                    problems.Add($"{d.Code}: attribute '{a.Key}' points at unknown lookup type '{a.LookupType}'.");
                if (a.Kind != LookupAttributeKind.Lookup && a.LookupType is not null)
                    problems.Add($"{d.Code}: attribute '{a.Key}' names a lookup type but is not a Lookup attribute.");
            }

            var seedCodes = new HashSet<string>();
            foreach (var s in d.Seeds)
            {
                if (!ValueCode().IsMatch(s.Code)) problems.Add($"{d.Code}: seed '{s.Code}' is not a valid code.");
                if (!seedCodes.Add(s.Code)) problems.Add($"{d.Code}: seed '{s.Code}' appears twice.");
                foreach (var key in s.Attributes?.Keys ?? [])
                    if (!keys.Contains(key)) problems.Add($"{d.Code}: seed '{s.Code}' has attribute '{key}' the type does not define.");
            }
        }

        if (problems.Count > 0)
            throw new InvalidOperationException("The lookup definitions are invalid:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", problems));
    }

    public IReadOnlyList<LookupTypeDefinition> All { get; }

    public LookupTypeDefinition? Find(string code) => _byCode.GetValueOrDefault(code);
}
