using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Modules.Documents.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Messages;

namespace VMS.Modules.Documents.Services;

public interface IDocumentTypeService
{
    /// <summary>The tenant's document types, seeding the twelve platform defaults the first time they are asked for (S0-FND-12's lazy-seed idiom).</summary>
    Task<List<DocumentTypeModel>> ListAsync(string? appliesTo = null);
    Task<DocumentTypeModel> CreateAsync(SaveDocumentTypeRequest request);
    Task<DocumentTypeModel> UpdateAsync(int id, SaveDocumentTypeRequest request);
}

/// <summary>The document type master (FSD §23A.1): what decides everything about how one kind of document behaves.</summary>
internal sealed class DocumentTypeService(DocumentDbContext db, ITenantContext tenantContext, IMessageCatalogue messages) : IDocumentTypeService
{
    private Guid Tenant => tenantContext.TenantId;

    public async Task<List<DocumentTypeModel>> ListAsync(string? appliesTo = null)
    {
        await EnsureDefaultsAsync();
        var query = db.Types.AsNoTracking().Where(t => t.TenantId == Tenant);
        if (appliesTo is not null && Enum.TryParse<AppliesTo>(appliesTo, out var flag))
            query = query.Where(t => (t.AppliesTo & flag) == flag);
        var rows = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private static DocumentTypeModel ToModel(DocumentType t) => new()
    {
        Id = t.DocumentTypeId, Code = t.Code, Name = t.Name, AppliesTo = SplitFlags(t.AppliesTo), PartnerRole = t.PartnerRole, IsExpirable = t.IsExpirable,
        DefaultValidityValue = t.DefaultValidityValue, DefaultValidityUnit = t.DefaultValidityUnit, IsPeriodic = t.IsPeriodic, RenewalLeadDays = t.RenewalLeadDays,
        MandatoryLevel = t.MandatoryLevel, RequiresDocumentNumber = t.RequiresDocumentNumber, HasCost = t.HasCost, AllowedFormats = SplitKinds((FileKinds)t.AllowedFormats),
        MaxFileSizeMb = t.MaxFileSizeMb, RetentionYears = t.RetentionYears, IsActive = t.IsActive,
        LinkedChargeTypeCode = DocumentChargeLink.ChargeTypeCodeByDocumentTypeCode.GetValueOrDefault(t.Code),
    };

    private static List<string> SplitFlags(AppliesTo value)
    {
        var list = new List<string>();
        if (value.HasFlag(Domain.AppliesTo.BusinessPartner)) list.Add(DocumentOwnerTypes.BusinessPartner);
        if (value.HasFlag(Domain.AppliesTo.Vehicle)) list.Add(DocumentOwnerTypes.Vehicle);
        return list;
    }

    private static List<string> SplitKinds(FileKinds value) => Enum.GetValues<FileKinds>().Where(k => k != FileKinds.None && value.HasFlag(k)).Select(k => k.ToString()).ToList();

    // ── Admin create and update ────────────────────────────────────────────────────

    public async Task<DocumentTypeModel> CreateAsync(SaveDocumentTypeRequest request)
    {
        await EnsureDefaultsAsync();
        var (type, errors) = await PrepareAsync(request, existing: null);
        if (type is null) throw new ValidationException(errors);
        db.Types.Add(type);
        await db.SaveChangesAsync();
        return ToModel(type);
    }

    public async Task<DocumentTypeModel> UpdateAsync(int id, SaveDocumentTypeRequest request)
    {
        var existing = await db.Types.FirstOrDefaultAsync(t => t.TenantId == Tenant && t.DocumentTypeId == id) ?? throw new NotFoundException("Document type not found.");
        var (type, errors) = await PrepareAsync(request, existing);
        if (type is null) throw new ValidationException(errors);
        await db.SaveChangesAsync();
        return ToModel(existing);
    }

    private async Task<(DocumentType? Type, List<ValidationError> Errors)> PrepareAsync(SaveDocumentTypeRequest r, DocumentType? existing)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var code = (r.Code ?? existing?.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (r.Name ?? string.Empty).Trim();
        if (existing is null)
        {
            if (code.Length == 0) Add("code", Msg.Required, ("Field", "Code"));
            else if (await db.Types.AnyAsync(t => t.TenantId == Tenant && t.Code == code)) Add("code", Msg.DocTypeCodeInUse);
        }
        if (name.Length == 0) Add("name", Msg.Required, ("Field", "Name"));
        else if (name.Length > 80) Add("name", Msg.MaxLength, ("Field", "Name"), ("Max", 80));

        var appliesTo = Domain.AppliesTo.None;
        foreach (var value in r.AppliesTo ?? [])
        {
            if (value == DocumentOwnerTypes.BusinessPartner) appliesTo |= Domain.AppliesTo.BusinessPartner;
            else if (value == DocumentOwnerTypes.Vehicle) appliesTo |= Domain.AppliesTo.Vehicle;
            else Add("appliesTo", Msg.OneOf, ("Field", "Applies to"), ("Allowed", string.Join(", ", DocumentOwnerTypes.All)));
        }
        if (appliesTo == Domain.AppliesTo.None) Add("appliesTo", Msg.Required, ("Field", "Applies to"));
        if (!string.IsNullOrWhiteSpace(r.PartnerRole) && !appliesTo.HasFlag(Domain.AppliesTo.BusinessPartner))
            Add("partnerRole", Msg.Invalid, ("Field", "Role"));

        if (r.IsExpirable && r.DefaultValidityValue is not (null or > 0)) Add("defaultValidityValue", Msg.Min, ("Field", "Default validity"), ("Min", "1"));
        if (r.DefaultValidityUnit is { } unit && !ValidityUnits.All.Contains(unit)) Add("defaultValidityUnit", Msg.OneOf, ("Field", "Validity unit"), ("Allowed", string.Join(", ", ValidityUnits.All)));

        var mandatory = r.MandatoryLevel ?? MandatoryLevels.None;
        if (!MandatoryLevels.All.Contains(mandatory)) Add("mandatoryLevel", Msg.OneOf, ("Field", "Mandatory level"), ("Allowed", string.Join(", ", MandatoryLevels.All)));

        var formats = FileKinds.None;
        foreach (var value in r.AllowedFormats is { Count: > 0 } ? r.AllowedFormats : ["Pdf", "Png", "Jpeg"])
        {
            if (Enum.TryParse<FileKinds>(value, out var kind)) formats |= kind;
            else Add("allowedFormats", Msg.Invalid, ("Field", "Allowed formats"));
        }

        var leadDays = r.RenewalLeadDays ?? 30;
        if (leadDays is < 0 or > 365) Add("renewalLeadDays", Msg.Invalid, ("Field", "Renewal lead days"));
        var maxSize = r.MaxFileSizeMb ?? 10;
        if (maxSize is < 1 or > 100) Add("maxFileSizeMb", Msg.Invalid, ("Field", "Max file size"));
        var retention = r.RetentionYears ?? 7;
        if (retention is < 1 or > 50) Add("retentionYears", Msg.Invalid, ("Field", "Retention years"));

        if (errors.Count > 0) return (null, errors);

        var type = existing ?? new DocumentType { Code = code };
        type.Name = name;
        type.AppliesTo = appliesTo;
        type.PartnerRole = string.IsNullOrWhiteSpace(r.PartnerRole) ? null : r.PartnerRole.Trim();
        type.IsExpirable = r.IsExpirable;
        type.DefaultValidityValue = r.IsExpirable ? r.DefaultValidityValue : null;
        type.DefaultValidityUnit = r.IsExpirable ? r.DefaultValidityUnit ?? ValidityUnits.Months : null;
        type.IsPeriodic = r.IsPeriodic;
        type.RenewalLeadDays = leadDays;
        type.MandatoryLevel = mandatory;
        type.RequiresDocumentNumber = r.RequiresDocumentNumber;
        type.HasCost = r.HasCost;
        type.AllowedFormats = (int)formats;
        type.MaxFileSizeMb = maxSize;
        type.RetentionYears = retention;
        type.IsActive = r.IsActive;
        return (type, errors);
    }

    // ── Starting a tenant's list from the defaults (S0-FND-12's idiom) ───────────────

    private async Task EnsureDefaultsAsync()
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) return;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction, "SELECT CASE WHEN EXISTS (SELECT 1 FROM doc.DocumentTypes WHERE TenantId = @tenant) THEN 1 ELSE 0 END", ("@tenant", tenant)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 1) return;
            }

            const FileKinds defaultFormats = FileKinds.Pdf | FileKinds.Png | FileKinds.Jpeg;
            var parameters = new List<(string, object?)> { ("@tenant", tenant) };
            var rows = new List<string>();
            for (var i = 0; i < PlatformDocumentTypes.Defaults.Count; i++)
            {
                var s = PlatformDocumentTypes.Defaults[i];
                parameters.Add(($"@code{i}", s.Code));
                parameters.Add(($"@name{i}", s.Name));
                parameters.Add(($"@applies{i}", (int)s.AppliesTo));
                parameters.Add(($"@role{i}", s.PartnerRole));
                parameters.Add(($"@exp{i}", s.IsExpirable));
                parameters.Add(($"@vv{i}", s.DefaultValidityValue));
                parameters.Add(($"@vu{i}", s.DefaultValidityUnit));
                parameters.Add(($"@per{i}", s.IsPeriodic));
                parameters.Add(($"@mand{i}", s.MandatoryLevel));
                parameters.Add(($"@num{i}", s.RequiresDocumentNumber));
                parameters.Add(($"@cost{i}", s.HasCost));
                parameters.Add(($"@sort{i}", (i + 1) * 10));
                rows.Add($"(@tenant, @code{i}, @name{i}, @applies{i}, @role{i}, @exp{i}, @vv{i}, @vu{i}, @per{i}, 30, @mand{i}, @num{i}, @cost{i}, {(int)defaultFormats}, 10, 7, 1, @sort{i})");
            }

            await using var insert = RawSql.Command(connection, transaction,
                $@"SET XACT_ABORT ON;
                   BEGIN TRAN;
                   IF NOT EXISTS (SELECT 1 FROM doc.DocumentTypes WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant)
                       INSERT INTO doc.DocumentTypes
                           (TenantId, Code, Name, AppliesTo, PartnerRole, IsExpirable, DefaultValidityValue, DefaultValidityUnit, IsPeriodic, RenewalLeadDays,
                            MandatoryLevel, RequiresDocumentNumber, HasCost, AllowedFormats, MaxFileSizeMb, RetentionYears, IsActive, SortOrder)
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
}
