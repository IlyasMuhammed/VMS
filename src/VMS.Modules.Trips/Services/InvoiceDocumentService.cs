using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Tenancy;

namespace VMS.Modules.Trips.Services;

/// <summary>Invoice PDF rendering (§34). First-time generation only for CC-27 — the SystemStandard layout is the
/// one built here; customer-specific layouts stay blocked on Q2's sample documents (documented, not guessed).</summary>
public interface IInvoiceDocumentService
{
    /// <summary>"Re-print returns the stored file" — generates the first version if none exists yet, otherwise
    /// hands back the already-stored current one without touching it.</summary>
    Task<InvoiceDocumentDownloadModel> PrintAsync(long invoiceId, int userId, CancellationToken ct = default);

    /// <summary>Admin-only (§34): always creates a new document version, never alters the invoice's own data.</summary>
    Task<InvoiceDocumentDownloadModel> RerenderAsync(long invoiceId, int userId, CancellationToken ct = default);
}

internal sealed class InvoiceDocumentService(
    TripsDbContext db, ITenantContext tenant, IFileStore files, IFileDownloadLinks links, ITenantProfileDirectory tenantProfile, IInvoicePdfRenderer renderer)
    : IInvoiceDocumentService
{
    public async Task<InvoiceDocumentDownloadModel> PrintAsync(long invoiceId, int userId, CancellationToken ct = default)
    {
        var current = await db.InvoiceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenant.TenantId && d.InvoiceId == invoiceId && d.IsCurrent, ct);
        current ??= await GenerateAsync(invoiceId, userId, ct);
        return ToDownload(current);
    }

    public async Task<InvoiceDocumentDownloadModel> RerenderAsync(long invoiceId, int userId, CancellationToken ct = default)
    {
        var next = await GenerateAsync(invoiceId, userId, ct);
        return ToDownload(next);
    }

    private async Task<InvoiceDocument> GenerateAsync(long invoiceId, int userId, CancellationToken ct)
    {
        // Every field read here is the invoice's own already-stored snapshot — never a live Customer/Trip/
        // Vehicle/Partner table — so "uses snapshots only" holds regardless of what has changed since generation.
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId).ToListAsync(ct);
        var adjustments = await db.InvoiceAdjustments.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.InvoiceId == invoiceId).OrderBy(a => a.Sequence).ToListAsync(ct);
        var taxLines = await db.InvoiceTaxLines.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.InvoiceId == invoiceId).OrderBy(t => t.Sequence).ToListAsync(ct);
        var profile = await tenantProfile.FindAsync(tenant.TenantId, ct);

        var data = new InvoicePdfData(
            profile?.Name ?? string.Empty, profile?.Address, profile?.ContactEmail, profile?.ContactPhone,
            invoice.InvoiceNumber, invoice.Version, invoice.InvoiceDate, invoice.DueDate, invoice.PeriodFrom, invoice.PeriodTo,
            invoice.BillToAddressName ?? invoice.CustomerName, invoice.BillToAddressLine1, invoice.BillToAddressLine2,
            invoice.BillToProvinceState, invoice.BillToPostalCode, invoice.BillToNtn, invoice.BillToStrn,
            invoice.CustomerCode, invoice.CustomerName, invoice.Ntn, invoice.Strn, invoice.CurrencyCode,
            lines.Select(l => new InvoicePdfLine(l.VehicleRegNo, l.TripNumber, l.TripDate, l.Description, l.Quantity, l.Rate, l.Amount)).ToList(),
            adjustments.Select(a => new InvoicePdfAdjustment(a.AdjustmentMonth, a.AdjustmentAmount, a.AdjustmentNote)).ToList(),
            taxLines.Select(t => new InvoicePdfTaxLine(t.TaxName, t.TaxCode, t.Applicable, t.Amount)).ToList(),
            invoice.TotalTripAmount, invoice.TotalAdjustment, invoice.GrossAmount, invoice.TotalDeduction, invoice.NetAmount);

        var bytes = renderer.Render(data);
        using var stream = new MemoryStream(bytes);
        var stored = await files.SaveAsync(new FileUpload(stream, $"{invoice.InvoiceNumber}.pdf", new FileOwner("Invoice", invoiceId.ToString()), FileRules.Scans, tenant.TenantId), ct);

        // Parenthesised deliberately: "1 + null" is null, so "?? 1" is the correct *first-version* fallback here,
        // not a defaulted-then-incremented count — the same intentional (if easy to misread) shape this module's
        // own CustomerInvoiceTemplateService already uses for the identical "next version number" calculation.
        var nextVersion = (1 + await db.InvoiceDocuments.Where(d => d.TenantId == tenant.TenantId && d.InvoiceId == invoiceId).Select(d => (int?)d.Version).MaxAsync(ct)) ?? 1;
        await db.InvoiceDocuments.Where(d => d.TenantId == tenant.TenantId && d.InvoiceId == invoiceId && d.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsCurrent, false), ct);

        var document = new InvoiceDocument
        {
            InvoiceId = invoiceId, DocumentType = InvoiceDocumentTypes.InvoicePdf, Version = nextVersion, StorageKey = stored.StorageKey,
            Sha256 = stored.Sha256, SizeBytes = stored.SizeBytes, ContentType = stored.ContentType, OriginalFileName = stored.OriginalFileName,
            GeneratedBy = userId, GeneratedAtUtc = DateTime.UtcNow, IsCurrent = true
        };
        db.InvoiceDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }

    // links.Create is synchronous (it just signs a URL, no I/O) — no await needed here.
    private InvoiceDocumentDownloadModel ToDownload(InvoiceDocument document)
    {
        var stored = new StoredFile(document.StorageKey, document.Sha256, document.SizeBytes, document.ContentType, document.OriginalFileName, document.GeneratedAtUtc);
        var link = links.Create(tenant.TenantId, stored, inline: true);
        return new InvoiceDocumentDownloadModel { Document = ToModel(document), Url = link.Url, ExpiresAtUtc = link.ExpiresAtUtc };
    }

    private static InvoiceDocumentModel ToModel(InvoiceDocument d) => new()
    {
        InvoiceDocumentId = d.InvoiceDocumentId, InvoiceId = d.InvoiceId, Version = d.Version, IsCurrent = d.IsCurrent,
        SizeBytes = d.SizeBytes, GeneratedAtUtc = d.GeneratedAtUtc, GeneratedBy = d.GeneratedBy, Sha256 = d.Sha256
    };
}
