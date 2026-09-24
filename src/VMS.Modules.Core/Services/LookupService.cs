using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Core.Data;
using VMS.Modules.Core.Domain;
using VMS.Modules.Core.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;

namespace VMS.Modules.Core.Services;

public interface ILookupAdminService
{
    Task<List<LookupTypeModel>> GetTypesAsync();

    /// <summary>Every value of the list, retired ones included, for the administration screen.</summary>
    Task<List<LookupItem>> GetValuesAsync(string lookupType);

    Task<LookupItem> CreateAsync(string lookupType, CreateLookupRequest request);

    Task<LookupItem> UpdateAsync(string lookupType, int id, UpdateLookupRequest request);
}

/// <summary>
/// The tenant's master lists (FSD §24.2). Each list starts from its defaults the first time it is used,
/// then belongs to the tenant: values are added, renamed, reordered and retired, never deleted.
/// </summary>
internal sealed partial class LookupService(CoreDbContext db, ITenantContext tenantContext, ILookupCatalog catalog)
    : ILookupReader, ILookupAdminService
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_]{0,29}$")]
    private static partial Regex CodePattern();

    private const int MaxDescription = 200;
    private const int MaxSortOrder = 9999;

    private Guid TenantId => tenantContext.TenantId;

    // ── Reading, for every module ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<LookupItem>> GetActiveAsync(string lookupType, IReadOnlyDictionary<string, string>? attributeFilter = null)
    {
        var definition = Define(lookupType);
        var filter = ValidateFilter(definition, attributeFilter);
        await EnsureDefaultsAsync(definition);
        var tenant = TenantId;

        var rows = await db.LookupValues.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.LookupType == lookupType && v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Description)
            .ToListAsync();

        var items = rows.Select(ToItem);
        // Lists are small (tens to hundreds of rows), so the filter runs here, on the parsed fields.
        foreach (var (key, wanted) in filter)
            items = items.Where(i => string.Equals(i.Attributes.GetValueOrDefault(key), wanted, StringComparison.OrdinalIgnoreCase));
        return items.ToList();
    }

    private static Dictionary<string, string> ValidateFilter(LookupTypeDefinition definition, IReadOnlyDictionary<string, string>? filter)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in filter ?? new Dictionary<string, string>())
        {
            if (definition.Schema.All(a => a.Key != key))
                throw new BadRequestException($"'{key}' is not a field of {definition.Name}.");
            result[key] = value.Trim();
        }
        return result;
    }

    public async Task<LookupItem?> FindAsync(string lookupType, int id)
    {
        Define(lookupType);
        var tenant = TenantId;
        var row = await db.LookupValues.AsNoTracking()
            .FirstOrDefaultAsync(v => v.TenantId == tenant && v.LookupType == lookupType && v.LookupValueID == id);
        return row is null ? null : ToItem(row);
    }

    public async Task<IReadOnlyDictionary<int, LookupItem>> FindManyAsync(string lookupType, IEnumerable<int> ids)
    {
        Define(lookupType);
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<int, LookupItem>();
        var tenant = TenantId;
        var rows = await db.LookupValues.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.LookupType == lookupType && wanted.Contains(v.LookupValueID))
            .ToListAsync();
        return rows.ToDictionary(r => r.LookupValueID, ToItem);
    }

    public async Task<bool> IsActiveAsync(string lookupType, int id) => (await FindAsync(lookupType, id))?.IsActive == true;

    // ── Administration ──────────────────────────────────────────────────────────────

    public async Task<List<LookupTypeModel>> GetTypesAsync()
    {
        foreach (var definition in catalog.All) await EnsureDefaultsAsync(definition);

        var tenant = TenantId;
        var counts = await db.LookupValues.AsNoTracking()
            .Where(v => v.TenantId == tenant)
            .GroupBy(v => v.LookupType)
            .Select(g => new { Type = g.Key, Total = g.Count(), Active = g.Count(v => v.IsActive) })
            .ToDictionaryAsync(x => x.Type);

        return catalog.All.Select(d => new LookupTypeModel
        {
            Code = d.Code,
            Name = d.Name,
            Description = d.Description,
            Attributes = d.Schema.Select(a => new LookupAttributeModel
            {
                Key = a.Key, Label = a.Label, Kind = a.Kind.ToString(), Required = a.Required, LookupType = a.LookupType
            }).ToList(),
            ActiveCount = counts.GetValueOrDefault(d.Code)?.Active ?? 0,
            TotalCount = counts.GetValueOrDefault(d.Code)?.Total ?? 0
        }).ToList();
    }

    public async Task<List<LookupItem>> GetValuesAsync(string lookupType)
    {
        var definition = Define(lookupType);
        await EnsureDefaultsAsync(definition);
        var tenant = TenantId;

        var rows = await db.LookupValues.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.LookupType == lookupType)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Description)
            .ToListAsync();
        return rows.Select(ToItem).ToList();
    }

    public async Task<LookupItem> CreateAsync(string lookupType, CreateLookupRequest request)
    {
        var definition = Define(lookupType);
        await EnsureDefaultsAsync(definition);
        var tenant = TenantId;

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
            throw new BadRequestException("The code must be 1 to 30 capital letters, digits or underscores.");
        var description = CleanDescription(request.Description);
        var sortOrder = request.SortOrder ?? await NextSortOrderAsync(lookupType);
        if (sortOrder is < 0 or > MaxSortOrder) throw new BadRequestException($"The sort order must be between 0 and {MaxSortOrder}.");
        var attributes = await NormalizeAttributesAsync(definition, request.Attributes);

        if (await db.LookupValues.AnyAsync(v => v.TenantId == tenant && v.LookupType == lookupType && v.Code == code))
            throw new ConflictException($"A {definition.Name} with the code '{code}' already exists.");

        var row = new LookupValue
        {
            TenantId = tenant,
            LookupType = lookupType,
            Code = code,
            Description = description,
            SortOrder = sortOrder,
            IsActive = true,
            Attributes = Serialize(attributes)
        };
        db.LookupValues.Add(row);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Someone added the same code between our check and our insert.
            throw new ConflictException($"A {definition.Name} with the code '{code}' already exists.");
        }

        return ToItem(row);
    }

    public async Task<LookupItem> UpdateAsync(string lookupType, int id, UpdateLookupRequest request)
    {
        var definition = Define(lookupType);
        var tenant = TenantId;
        var row = await db.LookupValues.FirstOrDefaultAsync(v => v.TenantId == tenant && v.LookupType == lookupType && v.LookupValueID == id)
            ?? throw new NotFoundException($"{definition.Name} not found.");

        var description = CleanDescription(request.Description);
        if (request.SortOrder is < 0 or > MaxSortOrder) throw new BadRequestException($"The sort order must be between 0 and {MaxSortOrder}.");
        var attributes = await NormalizeAttributesAsync(definition, request.Attributes);

        if (row.IsActive && !request.IsActive) await EnsureNothingDependsOnAsync(definition, row);

        row.Description = description;
        row.SortOrder = request.SortOrder;
        row.IsActive = request.IsActive;
        row.Attributes = Serialize(attributes);
        await db.SaveChangesAsync();   // audited: what was renamed, moved, retired or re-flagged, and by whom

        return ToItem(row);
    }

    // ── Rules ───────────────────────────────────────────────────────────────────────

    private LookupTypeDefinition Define(string lookupType) =>
        catalog.Find(lookupType) ?? throw new NotFoundException("That list does not exist.");

    private static string CleanDescription(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) throw new BadRequestException("A description is required.");
        if (text.Length > MaxDescription) throw new BadRequestException($"The description can be at most {MaxDescription} characters.");
        return text;
    }

    private async Task<int> NextSortOrderAsync(string lookupType)
    {
        var tenant = TenantId;
        var highest = await db.LookupValues.Where(v => v.TenantId == tenant && v.LookupType == lookupType)
            .Select(v => (int?)v.SortOrder).MaxAsync();
        return Math.Min((highest ?? 0) + 10, MaxSortOrder);
    }

    /// <summary>
    /// Checks the extra fields against the list's definition and returns them in their stored form. A field the
    /// list does not define is refused, a flag is <c>true</c> or <c>false</c>, a number is a whole number, and a
    /// reference to another list must be to a value that can still be chosen.
    /// </summary>
    private async Task<Dictionary<string, string?>> NormalizeAttributesAsync(LookupTypeDefinition definition, Dictionary<string, string?>? sent)
    {
        sent ??= [];
        var known = definition.Schema.Select(a => a.Key).ToHashSet();
        var unknown = sent.Keys.FirstOrDefault(k => !known.Contains(k));
        if (unknown is not null) throw new BadRequestException($"'{unknown}' is not a field of {definition.Name}.");

        var result = new Dictionary<string, string?>();
        foreach (var attribute in definition.Schema)
        {
            var text = sent.GetValueOrDefault(attribute.Key)?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                if (attribute.Required) throw new BadRequestException($"{attribute.Label} is required.");
                if (attribute.Kind == LookupAttributeKind.Flag) result[attribute.Key] = "false";   // no answer is "no"
                continue;
            }

            result[attribute.Key] = attribute.Kind switch
            {
                LookupAttributeKind.Flag => text.ToLowerInvariant() is "true" or "false"
                    ? text.ToLowerInvariant()
                    : throw new BadRequestException($"{attribute.Label} must be true or false."),
                LookupAttributeKind.Number => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : throw new BadRequestException($"{attribute.Label} must be a whole number, zero or more."),
                LookupAttributeKind.Text => text.Length <= MaxDescription
                    ? text
                    : throw new BadRequestException($"{attribute.Label} can be at most {MaxDescription} characters."),
                LookupAttributeKind.Lookup => await ReferencedCodeAsync(attribute, text),
                _ => throw new InvalidOperationException($"Unhandled attribute kind {attribute.Kind}.")
            };
        }
        return result;
    }

    private async Task<string> ReferencedCodeAsync(LookupAttribute attribute, string text)
    {
        var referenced = catalog.Find(attribute.LookupType!)!;
        await EnsureDefaultsAsync(referenced);
        var code = text.ToUpperInvariant();
        var tenant = TenantId;

        if (!await db.LookupValues.AnyAsync(v => v.TenantId == tenant && v.LookupType == referenced.Code && v.Code == code && v.IsActive))
            throw new BadRequestException($"'{text}' is not an active {referenced.Name}.");
        return code;
    }

    /// <summary>Retiring a value that another list still points at would leave those values referring to something that can no longer be chosen.</summary>
    private async Task EnsureNothingDependsOnAsync(LookupTypeDefinition definition, LookupValue row)
    {
        var tenant = TenantId;
        foreach (var other in catalog.All)
        {
            foreach (var attribute in other.Schema.Where(a => a.Kind == LookupAttributeKind.Lookup && a.LookupType == definition.Code))
            {
                // A list the tenant has not opened yet has no rows to block anything, but it will be created from
                // defaults that point at this value. Create it now, so the rule holds whatever order screens are visited in.
                await EnsureDefaultsAsync(other);

                var candidates = await db.LookupValues.AsNoTracking()
                    .Where(v => v.TenantId == tenant && v.LookupType == other.Code && v.IsActive)
                    .ToListAsync();
                var using_ = candidates.Count(v => Parse(v.Attributes).GetValueOrDefault(attribute.Key) == row.Code);
                if (using_ > 0)
                    throw new ConflictException($"{row.Description} cannot be retired: {using_} active {other.Name} value{(using_ == 1 ? " uses" : "s use")} it. Move or retire those first.");
            }
        }
    }

    // ── Starting a tenant's list from the defaults ──────────────────────────────────

    /// <summary>
    /// Creates the tenant's copy of a list from its defaults the first time it is used. A list that has any
    /// rows is left alone, so a tenant's edits (and a value it retired) are never put back. Safe if two
    /// requests arrive together: the second waits, finds the rows, and does nothing. Written with SQL rather
    /// than through the change tracker so the starting values are not logged as if the viewer had typed them.
    /// </summary>
    private async Task EnsureDefaultsAsync(LookupTypeDefinition definition)
    {
        if (definition.Seeds.Count == 0) return;
        var tenant = TenantId;
        if (tenant == Guid.Empty) throw new InvalidOperationException("No tenant to prepare the list for.");

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction,
                             "SELECT CASE WHEN EXISTS (SELECT 1 FROM core.LookupValues WHERE TenantId = @tenant AND LookupType = @type) THEN 1 ELSE 0 END",
                             ("@tenant", tenant), ("@type", definition.Code)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 1) return;
            }

            var parameters = new List<(string, object?)> { ("@tenant", tenant), ("@type", definition.Code) };
            var rows = new List<string>();
            for (var i = 0; i < definition.Seeds.Count; i++)
            {
                var seed = definition.Seeds[i];
                parameters.Add(($"@c{i}", seed.Code));
                parameters.Add(($"@d{i}", seed.Description));
                parameters.Add(($"@s{i}", (i + 1) * 10));
                parameters.Add(($"@a{i}", seed.Attributes is { Count: > 0 } ? Serialize(seed.Attributes) : null));
                rows.Add($"(@tenant, @type, @c{i}, @d{i}, @s{i}, 1, @a{i})");
            }

            await using var insert = RawSql.Command(connection, transaction,
                $@"SET XACT_ABORT ON;
                   BEGIN TRAN;
                   IF NOT EXISTS (SELECT 1 FROM core.LookupValues WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant AND LookupType = @type)
                       INSERT INTO core.LookupValues (TenantId, LookupType, Code, Description, SortOrder, IsActive, Attributes)
                       VALUES {string.Join(", ", rows)};
                   COMMIT;",
                parameters.ToArray());
            await insert.ExecuteNonQueryAsync();
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    // ── Mapping ─────────────────────────────────────────────────────────────────────

    private static LookupItem ToItem(LookupValue v) =>
        new(v.LookupValueID, v.Code, v.Description, v.SortOrder, v.IsActive, Parse(v.Attributes));

    private static Dictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? Serialize(IReadOnlyDictionary<string, string?> attributes) =>
        attributes.Count == 0 ? null : JsonSerializer.Serialize(attributes);
}
