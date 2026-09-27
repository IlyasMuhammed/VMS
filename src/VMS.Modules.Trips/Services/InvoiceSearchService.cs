using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Services;

/// <summary>The Invoice List (FSD §48.5 — see <see cref="InvoiceListItem"/>'s own doc comment for why this exists
/// despite not being one of the FSD's numbered screens). Only the invoice's own already-denormalized columns are
/// read — no cross-module lookup, unlike the Trip module's own equivalent gap-fill.</summary>
public interface IInvoiceSearchService
{
    Task<PaginatedResponse<InvoiceListItem>> SearchAsync(InvoiceSearchFilter filter, int page, int pageSize, CancellationToken ct = default);
}

internal sealed class InvoiceSearchService(TripsDbContext db, ITenantContext tenant) : IInvoiceSearchService
{
    public async Task<PaginatedResponse<InvoiceListItem>> SearchAsync(InvoiceSearchFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId);
        if (filter.CustomerId is { } customerId) query = query.Where(i => i.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(i => i.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus)) query = query.Where(i => i.PaymentStatus == filter.PaymentStatus);
        if (filter.FromDate is { } from) query = query.Where(i => i.InvoiceDate >= from);
        if (filter.ToDate is { } to) query = query.Where(i => i.InvoiceDate <= to);
        if (filter.IsActive is { } isActive) query = query.Where(i => i.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(i => i.InvoiceNumber.Contains(term) || i.CustomerName.Contains(term) || i.CustomerCode.Contains(term));
        }

        var total = await query.CountAsync(ct);
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 25 : Math.Min(pageSize, 200);
        var rows = await query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new InvoiceListItem
            {
                InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber, Version = i.Version, CustomerId = i.CustomerId, CustomerName = i.CustomerName,
                PeriodFrom = i.PeriodFrom, PeriodTo = i.PeriodTo, InvoiceDate = i.InvoiceDate, DueDate = i.DueDate, NetAmount = i.NetAmount, BalanceAmount = i.BalanceAmount,
                CurrencyCode = i.CurrencyCode, Status = i.Status, PaymentStatus = i.PaymentStatus, IsActive = i.IsActive,
            }).ToListAsync(ct);

        return new PaginatedResponse<InvoiceListItem> { Items = rows, TotalCount = total, Page = page, PageSize = pageSize };
    }
}
