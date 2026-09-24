using Microsoft.EntityFrameworkCore;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Modules.Documents.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Messages;
using VMS.Shared.Notifications;
using VMS.Shared.Numbering;

namespace VMS.Modules.Documents.Services;

public interface IDocumentService
{
    /// <summary>Every applicable type for this owner as a slot (its current version, if any, and history on request), plus any document of a type since retired from the master.</summary>
    Task<List<DocumentSlotModel>> ListAsync(string ownerType, int ownerId, bool includeHistory = false);
    Task<DocumentModel> UploadAsync(string ownerType, int ownerId, UploadDocumentRequest request, Stream content, string fileName, DocumentCaller caller);
    Task<DocumentModel> RenewAsync(string ownerType, int ownerId, int documentTypeId, RenewDocumentRequest request, Stream content, string fileName, DocumentCaller caller);
    Task<DocumentModel> RejectAsync(int documentId, RejectDocumentRequest request, DocumentCaller caller);
    Task<DownloadLinkModel> DownloadLinkAsync(int documentId, DocumentCaller caller);
}

/// <summary>
/// One document engine for both owners (FSD §23A): a slot per owner and type holding a current version and a history of
/// superseded ones, never replaced in place (BR-DOC-001). A version is never physically deleted here; only the retention job
/// removes one, once its type's retention period has passed (BR-DOC-002).
/// </summary>
internal sealed class DocumentService(DocumentContext ctx, IDocumentTypeService types, IFileStore files, IFileDownloadLinks links, INumberSeries series, IAuditContext audit, INotificationTrigger notifications) : IDocumentService
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<List<DocumentSlotModel>> ListAsync(string ownerType, int ownerId, bool includeHistory = false)
    {
        var roles = await ctx.OwnerRolesAsync(ownerType, ownerId);
        var applicable = (await types.ListAsync(ownerType))
            .Where(t => t.PartnerRole is null || roles.Contains(t.PartnerRole))
            .ToList();

        var rows = await ctx.Db.Documents.AsNoTracking().Where(d => d.TenantId == ctx.Tenant && d.OwnerType == ownerType && d.OwnerId == ownerId)
            .OrderByDescending(d => d.VersionNo).ToListAsync();
        var typeNames = await ctx.Db.Types.AsNoTracking().Where(t => t.TenantId == ctx.Tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.Name);
        var today = await ctx.TodayAsync();

        var slots = applicable.Select(t => new DocumentSlotModel { DocumentTypeId = t.Id, DocumentTypeName = t.Name, MandatoryLevel = t.MandatoryLevel, HasCost = t.HasCost }).ToList();
        // A document of a type since retired or narrowed away still needs somewhere to show.
        foreach (var typeId in rows.Select(r => r.DocumentTypeId).Distinct().Except(slots.Select(s => s.DocumentTypeId)))
            slots.Add(new DocumentSlotModel { DocumentTypeId = typeId, DocumentTypeName = typeNames.GetValueOrDefault(typeId, "Unknown type") });

        foreach (var slot in slots)
        {
            var forType = rows.Where(r => r.DocumentTypeId == slot.DocumentTypeId).ToList();
            slot.Current = forType.Where(r => r.IsCurrent).Select(r => ToModel(r, today)).FirstOrDefault();
            if (includeHistory) slot.History = forType.Where(r => !r.IsCurrent).Select(r => ToModel(r, today)).ToList();
        }
        return slots.OrderBy(s => s.DocumentTypeName).ToList();
    }

    private static DocumentModel ToModel(Document d, DateOnly today) => new()
    {
        Id = d.DocumentId, DocumentCode = d.DocumentCode, OwnerType = d.OwnerType, OwnerId = d.OwnerId, DocumentTypeId = d.DocumentTypeId,
        VersionNo = d.VersionNo, IsCurrent = d.IsCurrent, Status = d.Status, DocumentNumber = d.DocumentNumber, Provider = d.Provider,
        IssueDate = d.IssueDate, ExpiryDate = d.ExpiryDate, DaysRemaining = d.ExpiryDate is { } exp ? exp.DayNumber - today.DayNumber : null,
        OriginalFileName = d.OriginalFileName, SizeBytes = d.SizeBytes, RejectReason = d.RejectReason, LinkedTransactionId = d.LinkedTransactionId, CreatedOn = d.CreatedOn,
    };

    // ── Uploading and renewing ──────────────────────────────────────────────────────

    /// <summary>Checks the fields common to an upload and a renewal (BR-DOC-005): the document number if the type requires one, the expiry date if the type is expirable.</summary>
    private List<ValidationError> ValidateFields(DocumentType type, string? number, DateOnly? issue, DateOnly? expiry, DateOnly today)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));
        if (type.RequiresDocumentNumber && string.IsNullOrWhiteSpace(number)) Add("documentNumber", Msg.Required, ("Field", "Document number"));
        if (type.IsExpirable && expiry is null) Add("expiryDate", Msg.Required, ("Field", "Expiry date"));
        if (issue is { } iss && expiry is { } exp && exp <= iss) Add("expiryDate", Msg.VhEndBeforeStart);
        if (issue > today) Add("issueDate", Msg.NotFuture, ("Field", "Issue date"));
        return errors;
    }

    private static string StatusFor(DocumentType type, DateOnly? expiry, DateOnly today)
    {
        if (!type.IsExpirable || expiry is null) return DocumentStatuses.Active;
        if (expiry < today) return DocumentStatuses.Expired;
        if (expiry <= today.AddDays(type.RenewalLeadDays)) return DocumentStatuses.ExpiringSoon;
        return DocumentStatuses.Active;
    }

    public async Task<DocumentModel> UploadAsync(string ownerType, int ownerId, UploadDocumentRequest request, Stream content, string fileName, DocumentCaller caller)
    {
        if (!caller.Has(PermissionCodes.DOC_UPLOAD)) throw new ForbiddenException("You do not have permission to upload documents.");
        if (!DocumentOwnerTypes.All.Contains(ownerType) || !await ctx.OwnerExistsAsync(ownerType, ownerId)) throw new NotFoundException("Owner not found.");
        if (request.DocumentTypeId is not > 0) throw ctx.Error("documentTypeId", Msg.Required, ("Field", "Document type"));
        var type = await ctx.LoadTypeAsync(request.DocumentTypeId.Value);

        var already = await ctx.Db.Documents.AnyAsync(d => d.TenantId == ctx.Tenant && d.OwnerType == ownerType && d.OwnerId == ownerId && d.DocumentTypeId == type.DocumentTypeId && d.IsCurrent);
        if (already) throw ctx.Error("documentTypeId", Msg.DocAlreadyCurrent);   // a slot with a current version is renewed, not uploaded again

        var today = await ctx.TodayAsync();
        var issue = ParseDate(request.IssueDate);
        var expiry = ParseDate(request.ExpiryDate);
        var errors = ValidateFields(type, request.DocumentNumber, issue, expiry, today);
        if (errors.Count > 0) throw new ValidationException(errors);

        var highest = await ctx.Db.Documents.Where(d => d.TenantId == ctx.Tenant && d.OwnerType == ownerType && d.OwnerId == ownerId && d.DocumentTypeId == type.DocumentTypeId)
            .Select(d => (int?)d.VersionNo).MaxAsync();
        var nextVersion = 1 + (highest ?? 0);

        var doc = new Document
        {
            OwnerType = ownerType, OwnerId = ownerId, DocumentTypeId = type.DocumentTypeId, VersionNo = nextVersion, IsCurrent = true,
            Status = StatusFor(type, expiry, today), DocumentNumber = Blank(request.DocumentNumber), Provider = Blank(request.Provider),
            IssueDate = issue, ExpiryDate = expiry, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow,
        };
        return await SaveWithFileAsync(doc, type, content, fileName, superseding: null);
    }

    public async Task<DocumentModel> RenewAsync(string ownerType, int ownerId, int documentTypeId, RenewDocumentRequest request, Stream content, string fileName, DocumentCaller caller)
    {
        if (!caller.Has(PermissionCodes.DOC_RENEW)) throw new ForbiddenException("You do not have permission to renew documents.");
        if (!await ctx.OwnerExistsAsync(ownerType, ownerId)) throw new NotFoundException("Owner not found.");
        var type = await ctx.LoadTypeAsync(documentTypeId);
        var current = await ctx.Db.Documents.FirstOrDefaultAsync(d => d.TenantId == ctx.Tenant && d.OwnerType == ownerType && d.OwnerId == ownerId && d.DocumentTypeId == documentTypeId && d.IsCurrent)
            ?? throw ctx.Error("documentTypeId", Msg.DocNothingToRenew);

        var today = await ctx.TodayAsync();
        var issue = ParseDate(request.IssueDate) ?? today;
        // BR-DOC-005: pre-fills from the version being renewed and computes the new expiry, unless the caller sends its own.
        var expiry = ParseDate(request.ExpiryDate)
            ?? (type.IsExpirable && current.ExpiryDate is { } previous && type.DefaultValidityValue is { } value
                ? ValidityUnits.Add(previous, value, type.DefaultValidityUnit ?? ValidityUnits.Months)
                : (DateOnly?)null);
        var number = Blank(request.DocumentNumber) ?? current.DocumentNumber;
        var provider = Blank(request.Provider) ?? current.Provider;

        var errors = ValidateFields(type, number, issue, expiry, today);
        if (errors.Count > 0) throw new ValidationException(errors);

        var doc = new Document
        {
            OwnerType = ownerType, OwnerId = ownerId, DocumentTypeId = type.DocumentTypeId, VersionNo = current.VersionNo + 1, IsCurrent = true,
            Status = StatusFor(type, expiry, today), DocumentNumber = number, Provider = provider, IssueDate = issue, ExpiryDate = expiry,
            // BR-DOC-006: tags which ledger transaction paid for this, if the caller already confirmed the matching recurring charge
            // entry (the sole posting path — this never posts anything itself, so linking to any positive id is trusted at face value).
            LinkedTransactionId = request.LinkedTransactionId is > 0 ? request.LinkedTransactionId : null,
            CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow,
        };
        return await SaveWithFileAsync(doc, type, content, fileName, superseding: current);
    }

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var d) ? d : null;

    /// <summary>Numbers, stores the file, and writes the row in one transaction; a version already superseded is flipped in the same save (BR-DOC-001).</summary>
    private async Task<DocumentModel> SaveWithFileAsync(Document doc, DocumentType type, Stream content, string fileName, Document? superseding)
    {
        var today = await ctx.TodayAsync();
        await ctx.Db.InTransactionAsync(async ct =>
        {
            doc.DocumentCode = await series.NextAsync(ctx.Db, NumberSeriesCodes.Document, today, ctx.Tenant, ct);
            if (superseding is not null) superseding.IsCurrent = false;
            ctx.Db.Documents.Add(doc);
            if (superseding is not null) superseding.Status = DocumentStatuses.Superseded;
            await ctx.Db.SaveChangesAsync(ct);   // the id is needed for the file's folder name
        });

        var stored = await files.SaveAsync(new FileUpload(content, fileName, new FileOwner("Document", doc.DocumentId.ToString()), ctx.RulesFor(type), ctx.Tenant));
        doc.StorageKey = stored.StorageKey;
        doc.Sha256 = stored.Sha256;
        doc.ContentType = stored.ContentType;
        doc.OriginalFileName = stored.OriginalFileName;
        doc.SizeBytes = stored.SizeBytes;
        await ctx.Db.SaveChangesAsync();

        // FR-BP-016, FR-VH-008: picked up immediately, not left for the next hourly run.
        if (doc.ExpiryDate is not null) await notifications.NotifyAsync(nameof(Document), doc.DocumentId.ToString());

        return ToModel(doc, today);
    }

    // ── Rejecting (BR-DOC-008) ──────────────────────────────────────────────────────

    public async Task<DocumentModel> RejectAsync(int documentId, RejectDocumentRequest request, DocumentCaller caller)
    {
        if (!caller.Has(PermissionCodes.DOC_REJECT)) throw new ForbiddenException("You do not have permission to reject documents.");
        var doc = await ctx.Db.Documents.FirstOrDefaultAsync(d => d.TenantId == ctx.Tenant && d.DocumentId == documentId) ?? throw new NotFoundException("Document not found.");
        if (!doc.IsCurrent) throw new ConflictException("Only the current version can be rejected.");
        var reason = Blank(request.Reason);
        if (reason is null) throw ctx.Error("reason", Msg.Required, ("Field", "Reason"));

        // Rejected leaves the slot without a current version: a mandatory check reports it missing again, and the next upload is a fresh one, not a renewal.
        doc.IsCurrent = false;
        doc.Status = DocumentStatuses.Rejected;
        doc.RejectReason = reason.Length > 500 ? reason[..500] : reason;
        doc.ModifiedBy = caller.UserId;
        doc.ModifiedOn = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();
        return ToModel(doc, await ctx.TodayAsync());
    }

    // ── Downloading (BR-DOC-007, FR-DOC-001) ────────────────────────────────────────

    private static bool IsSensitive(string typeCode) => typeCode is PlatformDocumentTypes.Cnic or PlatformDocumentTypes.DrivingLicence;

    public async Task<DownloadLinkModel> DownloadLinkAsync(int documentId, DocumentCaller caller)
    {
        var doc = await ctx.Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == ctx.Tenant && d.DocumentId == documentId) ?? throw new NotFoundException("Document not found.");
        var type = await ctx.Db.Types.AsNoTracking().FirstAsync(t => t.DocumentTypeId == doc.DocumentTypeId);
        var needed = IsSensitive(type.Code) ? PermissionCodes.DOC_DOWNLOAD_SENSITIVE : PermissionCodes.DOC_DOWNLOAD;
        if (!caller.Has(needed)) throw new ForbiddenException("You do not have permission to download this document.");

        var stored = new StoredFile(doc.StorageKey, doc.Sha256, doc.SizeBytes, doc.ContentType, doc.OriginalFileName, doc.CreatedOn);
        var link = links.Create(ctx.Tenant, stored, inline: true);

        // A download has no row change of its own (S0-FND-08's note): the note rides the next save on this same audited context.
        audit.Note(new AuditNote("Document", doc.DocumentId.ToString(), "Downloaded", RootEntity: doc.OwnerType, RootRecordId: doc.OwnerId.ToString(),
            Reason: $"By {caller.UserName ?? caller.UserId.ToString()}"));
        await ctx.Db.SaveChangesAsync();

        return new DownloadLinkModel { Url = link.Url, ExpiresAtUtc = link.ExpiresAtUtc };
    }
}
