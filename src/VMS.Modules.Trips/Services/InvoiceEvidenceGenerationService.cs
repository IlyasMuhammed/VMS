using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Trips.Services;

/// <summary>§41: invoice evidence generation. Queued rows are created inside the same transaction as invoice
/// creation (see <c>InvoiceCreationService.CreateAsync</c>); this service is what actually turns a Queued row
/// into a Generated (or Failed) one, whether called from the background job, a Finance Retry, or an Admin
/// Rerender.</summary>
public interface IInvoiceEvidenceGenerationService
{
    /// <summary>The background job's own entry point (§41: "rendered by a background job") — generates every
    /// Queued row for the current tenant. Returns how many it processed.</summary>
    Task<int> ProcessQueuedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceEvidenceModel>> ListAsync(long invoiceId, CancellationToken ct = default);
    Task<InvoiceEvidenceDownloadModel> DownloadAsync(long invoiceEvidenceId, CancellationToken ct = default);
    /// <summary>Finance: "failure shows Retry" — regenerates the SAME (Failed) row in place, never a new version.</summary>
    Task<InvoiceEvidenceModel> RetryAsync(long invoiceEvidenceId, int userId, CancellationToken ct = default);
    /// <summary>Admin-only: always a new version (§41: "EvidenceVersion +1 only if Admin re-renders").</summary>
    Task<InvoiceEvidenceModel> RerenderAsync(long invoiceId, int userId, CancellationToken ct = default);
}

internal sealed class InvoiceEvidenceGenerationService(
    TripsDbContext db, ITenantContext tenant, IFileStore files, IFileDownloadLinks links, ITenantProfileDirectory tenantProfile, IInvoiceEvidenceRenderer renderer)
    : IInvoiceEvidenceGenerationService
{
    public async Task<int> ProcessQueuedAsync(CancellationToken ct = default)
    {
        var queued = await db.InvoiceEvidences.Where(e => e.TenantId == tenant.TenantId && e.Status == InvoiceEvidenceStatuses.Queued).ToListAsync(ct);
        foreach (var evidence in queued) await GenerateAsync(evidence, generatedBy: null, ct);
        if (queued.Count > 0) await db.SaveChangesAsync(ct);
        return queued.Count;
    }

    public async Task<IReadOnlyList<InvoiceEvidenceModel>> ListAsync(long invoiceId, CancellationToken ct = default)
    {
        var rows = await db.InvoiceEvidences.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId)
            .OrderByDescending(e => e.EvidenceVersion).ToListAsync(ct);
        var models = new List<InvoiceEvidenceModel>();
        foreach (var row in rows) models.Add(await ToModelAsync(row, ct));
        return models;
    }

    public async Task<InvoiceEvidenceDownloadModel> DownloadAsync(long invoiceEvidenceId, CancellationToken ct = default)
    {
        var evidence = await RequireAsync(invoiceEvidenceId, ct);
        if (evidence.Status != InvoiceEvidenceStatuses.Generated)
            // §41's own literal edge case text: "missing evidence at submit".
            throw new BusinessRuleException("EVIDENCE_NOT_READY", "Invoice evidence is not ready. Please wait or retry evidence generation.", []);
        var stored = new StoredFile(evidence.StorageKey, evidence.Sha256, evidence.SizeBytes, evidence.ContentType, evidence.OriginalFileName, evidence.GeneratedAtUtc ?? DateTime.UtcNow);
        var link = links.Create(tenant.TenantId, stored, inline: true);
        return new InvoiceEvidenceDownloadModel { Evidence = await ToModelAsync(evidence, ct), Url = link.Url, ExpiresAtUtc = link.ExpiresAtUtc };
    }

    public async Task<InvoiceEvidenceModel> RetryAsync(long invoiceEvidenceId, int userId, CancellationToken ct = default)
    {
        var evidence = await RequireAsync(invoiceEvidenceId, ct);
        if (evidence.Status != InvoiceEvidenceStatuses.Failed)
            throw new BusinessRuleException("EVIDENCE_NOT_FAILED", "Only a failed evidence generation can be retried.", []);
        await GenerateAsync(evidence, userId, ct);
        await db.SaveChangesAsync(ct);
        return await ToModelAsync(evidence, ct);
    }

    public async Task<InvoiceEvidenceModel> RerenderAsync(long invoiceId, int userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
        var nextVersion = 1 + (await db.InvoiceEvidences.Where(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId)
            .Select(e => (int?)e.EvidenceVersion).MaxAsync(ct) ?? 0);
        var evidence = new InvoiceEvidence
        {
            InvoiceId = invoiceId, EvidenceVersion = nextVersion, GroupBy = InvoiceEvidenceGroupBy.Vehicle,
            PageSize = invoice.EvidencePageSize ?? 50, Status = InvoiceEvidenceStatuses.Queued
        };
        db.InvoiceEvidences.Add(evidence);
        await GenerateAsync(evidence, userId, ct);
        await db.SaveChangesAsync(ct);
        return await ToModelAsync(evidence, ct);
    }

    private async Task<InvoiceEvidence> RequireAsync(long invoiceEvidenceId, CancellationToken ct) =>
        await db.InvoiceEvidences.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId && e.InvoiceEvidenceId == invoiceEvidenceId, ct)
            ?? throw new NotFoundException($"Invoice evidence {invoiceEvidenceId} was not found.");

    /// <summary>The one place that actually renders and stores a file — reached by the background job, by Retry
    /// (same version, row already exists) and by Rerender (a version the caller has just added). Never lets a
    /// rendering problem escape as an exception: §41's own Failed status exists exactly so a bad render becomes
    /// a retryable state, not a 500 that would abort the whole background sweep.</summary>
    private async Task GenerateAsync(InvoiceEvidence evidence, int? generatedBy, CancellationToken ct)
    {
        try
        {
            var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == evidence.InvoiceId, ct)
                ?? throw new NotFoundException($"Invoice {evidence.InvoiceId} was not found.");
            // §41: "evidence is built only from the invoice's own snapshots" — InvoiceLine, never a live
            // Trip/Vehicle/Route read. Income lines are excluded: evidence lists "the trips behind an invoice,"
            // and an Income line (§33) has no trip-level route/vehicle grouping to sit under.
            var lines = await db.InvoiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == evidence.InvoiceId && l.LineType == InvoiceLineTypes.Trip).ToListAsync(ct);

            var pageInputs = lines.Select(l => new InvoiceEvidencePagination.LineInput(
                l.VehicleRegNo ?? "—", l.TripDate, l.TripNumber, l.RouteLabel ?? string.Empty, l.CustomerTripReference, l.Amount)).ToList();
            var pagination = InvoiceEvidencePagination.Paginate(pageInputs, evidence.PageSize);

            var profile = await tenantProfile.FindAsync(tenant.TenantId, ct);
            var data = new InvoiceEvidenceData(
                profile?.Name ?? string.Empty, invoice.CustomerName, invoice.CustomerCode, invoice.InvoiceNumber, invoice.Version, invoice.InvoiceDate,
                invoice.PeriodFrom, invoice.PeriodTo, evidence.EvidenceVersion, invoice.CurrencyCode,
                invoice.TotalTripAmount, invoice.TotalAdjustment, invoice.GrossAmount, invoice.TotalDeduction, invoice.NetAmount);

            var bytes = renderer.Render(data, pagination);
            using var stream = new MemoryStream(bytes);
            var stored = await files.SaveAsync(new FileUpload(stream, $"{invoice.InvoiceNumber}-evidence-v{evidence.EvidenceVersion}.pdf",
                new FileOwner("Invoice", evidence.InvoiceId.ToString()), FileRules.Scans, tenant.TenantId), ct);

            evidence.StorageKey = stored.StorageKey; evidence.Sha256 = stored.Sha256; evidence.SizeBytes = stored.SizeBytes;
            evidence.ContentType = stored.ContentType; evidence.OriginalFileName = stored.OriginalFileName;
            evidence.PageCount = pagination.PageCount; evidence.LineCount = pagination.LineCount;
            evidence.Status = InvoiceEvidenceStatuses.Generated; evidence.ErrorMessage = null;
            evidence.GeneratedBy = generatedBy; evidence.GeneratedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            evidence.Status = InvoiceEvidenceStatuses.Failed;
            evidence.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
        }
    }

    /// <summary>Per-vehicle page ranges (AC-33's own shape) aren't stored on the row itself — only the aggregate
    /// <see cref="InvoiceEvidence.PageCount"/>/<see cref="InvoiceEvidence.LineCount"/> are — so a Generated row's
    /// model recomputes them from the same immutable InvoiceLine snapshot and the row's own stored PageSize,
    /// through the identical pure <see cref="InvoiceEvidencePagination"/> function the renderer used, rather than
    /// duplicating that breakdown into its own table.</summary>
    private async Task<InvoiceEvidenceModel> ToModelAsync(InvoiceEvidence e, CancellationToken ct)
    {
        var model = new InvoiceEvidenceModel
        {
            InvoiceEvidenceId = e.InvoiceEvidenceId, InvoiceId = e.InvoiceId, EvidenceVersion = e.EvidenceVersion, GroupBy = e.GroupBy,
            PageSize = e.PageSize, Status = e.Status, ErrorMessage = e.ErrorMessage, PageCount = e.PageCount, LineCount = e.LineCount,
            SizeBytes = e.SizeBytes, Sha256 = e.Sha256, GeneratedBy = e.GeneratedBy, GeneratedAtUtc = e.GeneratedAtUtc
        };
        if (e.Status != InvoiceEvidenceStatuses.Generated) return model;

        var lines = await db.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == e.InvoiceId && l.LineType == InvoiceLineTypes.Trip).ToListAsync(ct);
        var pagination = InvoiceEvidencePagination.Paginate(
            lines.Select(l => new InvoiceEvidencePagination.LineInput(l.VehicleRegNo ?? "—", l.TripDate, l.TripNumber, l.RouteLabel ?? string.Empty, l.CustomerTripReference, l.Amount)).ToList(),
            e.PageSize);
        model.VehiclePages = pagination.VehiclePages.Select(v => new InvoiceEvidenceVehiclePageModel
        {
            VehicleRegNo = v.VehicleRegNo, FirstPage = v.FirstPage, LastPage = v.LastPage, LineCount = v.LineCount, Subtotal = v.Subtotal
        }).ToList();
        return model;
    }
}
