using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.1: CUS-01..10.</summary>
internal sealed partial class LedgerReportService
{
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "customerName", "ntn", "strn", "currencyCode", "paymentTermsDays", "status", "createdOn" };
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId
            && (string.IsNullOrEmpty(f.Status) || c.Status == f.Status)).ToListAsync(ct);
        var created = await AuditCreatedInfoAsync("Customer", customers.Select(c => c.CustomerId.ToString()), ct);

        var rows = customers.Select(c => new Dictionary<string, object?>
        {
            ["customerCode"] = c.CustomerCode, ["customerName"] = c.CustomerName, ["ntn"] = c.Ntn, ["strn"] = c.Strn,
            ["currencyCode"] = c.CurrencyCode, ["paymentTermsDays"] = c.PaymentTermsDays, ["status"] = c.Status,
            ["createdOn"] = created.GetValueOrDefault(c.CustomerId.ToString()).On
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "name", "designation", "mobile1", "mobile2", "email", "availabilityTime" };
        var contacts = await db.CustomerContacts.AsNoTracking().Where(c => c.TenantId == tenant.TenantId
            && (f.CustomerId == null || c.CustomerId == f.CustomerId) && (string.IsNullOrEmpty(f.Status) || c.Status == f.Status)).ToListAsync(ct);
        if (contacts.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(contacts.Select(c => c.CustomerId), ct);
        var rows = contacts.Select(c => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(c.CustomerId), ["name"] = c.Name, ["designation"] = c.Designation,
            ["mobile1"] = c.Mobile1, ["mobile2"] = c.Mobile2, ["email"] = c.Email, ["availabilityTime"] = c.AvailabilityTime
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "addressName", "fullAddress", "ntn", "isDefault", "effectiveFrom", "effectiveTo" };
        var addresses = await db.CustomerBillingAddresses.AsNoTracking().Where(a => a.TenantId == tenant.TenantId
            && (f.CustomerId == null || a.CustomerId == f.CustomerId) && (string.IsNullOrEmpty(f.Status) || a.Status == f.Status)).ToListAsync(ct);
        if (addresses.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(addresses.Select(a => a.CustomerId), ct);
        var rows = addresses.Select(a => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(a.CustomerId), ["addressName"] = a.AddressName,
            ["fullAddress"] = string.Join(", ", new[] { a.AddressLine1, a.AddressLine2 }.Where(s => !string.IsNullOrWhiteSpace(s))),
            ["ntn"] = a.Ntn, ["isDefault"] = a.IsDefault, ["effectiveFrom"] = a.EffectiveFrom, ["effectiveTo"] = a.EffectiveTo
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "taxName", "taxCode", "taxType", "rateOrFixed", "calculationBasis", "applicable", "effectiveFrom", "effectiveTo", "status" };
        var rules = await db.CustomerTaxRules.AsNoTracking().Where(r => r.TenantId == tenant.TenantId
            && (f.CustomerId == null || r.CustomerId == f.CustomerId)
            && (f.AsOf == null || (r.EffectiveFrom <= f.AsOf && (r.EffectiveTo == null || r.EffectiveTo >= f.AsOf)))).ToListAsync(ct);
        if (rules.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(rules.Select(r => r.CustomerId), ct);
        var rows = rules.Select(r => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(r.CustomerId), ["taxName"] = r.TaxName, ["taxCode"] = r.TaxCode, ["taxType"] = r.TaxType,
            ["rateOrFixed"] = r.TaxType == TaxTypes.Percentage ? r.TaxPercentage : r.FixedAmount, ["calculationBasis"] = r.CalculationBasis,
            ["applicable"] = r.Applicable, ["effectiveFrom"] = r.EffectiveFrom, ["effectiveTo"] = r.EffectiveTo, ["status"] = r.Status
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "tripCode", "routeCode", "stopCount", "allowedVehicleCount", "currentRate", "nextRateChange" };
        var configs = await db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId
            && (f.CustomerId == null || c.CustomerId == f.CustomerId) && (string.IsNullOrEmpty(f.Status) || c.Status == f.Status)).ToListAsync(ct);
        if (configs.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(configs.Select(c => c.CustomerId), ct);
        var configIds = configs.Select(c => c.TripConfigurationId).ToList();
        var routeIds = configs.Select(c => c.RouteId).Distinct().ToList();
        var routes = await db.Routes.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && routeIds.Contains(r.RouteId)).ToDictionaryAsync(r => r.RouteId, r => r.RouteCode, ct);
        var stopCounts = await db.TripConfigurationStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && configIds.Contains(s.TripConfigurationId))
            .GroupBy(s => s.TripConfigurationId).Select(g => new { ConfigId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.ConfigId, g => g.Count, ct);
        var vehicleCounts = await db.TripConfigurationVehicles.AsNoTracking().Where(v => v.TenantId == tenant.TenantId && configIds.Contains(v.TripConfigurationId) && v.Status == ActiveInactiveStatuses.Active)
            .GroupBy(v => v.TripConfigurationId).Select(g => new { ConfigId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.ConfigId, g => g.Count, ct);
        var today = await clock.TodayAsync(tenant.TenantId);
        var rates = await db.TripRates.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && configIds.Contains(r.TripConfigurationId) && r.Status == ActiveInactiveStatuses.Active).ToListAsync(ct);

        var rows = configs.Select(c =>
        {
            var configRates = rates.Where(r => r.TripConfigurationId == c.TripConfigurationId).ToList();
            var current = configRates.Where(r => r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today)).OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
            var next = configRates.Where(r => r.EffectiveFrom > today).OrderBy(r => r.EffectiveFrom).FirstOrDefault();
            return new Dictionary<string, object?>
            {
                ["customerCode"] = customerCodes.GetValueOrDefault(c.CustomerId), ["tripCode"] = c.TripCode, ["routeCode"] = routes.GetValueOrDefault(c.RouteId),
                ["stopCount"] = stopCounts.GetValueOrDefault(c.TripConfigurationId, 0), ["allowedVehicleCount"] = vehicleCounts.GetValueOrDefault(c.TripConfigurationId, 0),
                ["currentRate"] = current?.RateAmount, ["nextRateChange"] = next?.EffectiveFrom
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus06Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "fixedTrips", "openTrips", "completedTrips", "cancelledTrips", "tripAmount" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId
            && (f.From == null || t.TripDate >= f.From) && (f.To == null || t.TripDate <= f.To) && (f.CustomerId == null || t.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(trips.Select(t => t.CustomerId), ct);
        var rows = trips.GroupBy(t => t.CustomerId).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(g.Key), ["fixedTrips"] = g.Count(t => t.TripType == TripTypes.Fixed),
            ["openTrips"] = g.Count(t => t.TripType == TripTypes.Open), ["completedTrips"] = g.Count(t => t.Status == TripStatuses.Completed),
            ["cancelledTrips"] = g.Count(t => t.Status == TripStatuses.Cancelled), ["tripAmount"] = g.Sum(t => t.TripAmount ?? 0)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus07Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "invoices", "gross", "deductions", "net", "paid", "balance" };
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId
            && (f.From == null || i.InvoiceDate >= f.From) && (f.To == null || i.InvoiceDate <= f.To) && (f.CustomerId == null || i.CustomerId == f.CustomerId)
            && (string.IsNullOrEmpty(f.Status) || i.Status == f.Status)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(invoices.Select(i => i.CustomerId), ct);
        var rows = invoices.GroupBy(i => i.CustomerId).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(g.Key), ["invoices"] = g.Count(), ["gross"] = g.Sum(i => i.GrossAmount),
            ["deductions"] = g.Sum(i => i.TotalDeduction), ["net"] = g.Sum(i => i.NetAmount), ["paid"] = g.Sum(i => i.PaidAmount), ["balance"] = g.Sum(i => i.BalanceAmount)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus08Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "receiptNumber", "receiptDate", "method", "instrumentNo", "amount", "invoicesAllocated", "status" };
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId
            && (f.CustomerId == null || r.CustomerId == f.CustomerId) && (f.From == null || r.ReceiptDate >= f.From) && (f.To == null || r.ReceiptDate <= f.To)
            && (string.IsNullOrEmpty(f.Method) || r.PaymentMethod == f.Method)).ToListAsync(ct);
        if (receipts.Count == 0) return (columns, []);

        var receiptIds = receipts.Select(r => r.CustomerReceiptId).ToList();
        var allocationCounts = await db.InvoicePayments.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && receiptIds.Contains(p.CustomerReceiptId))
            .GroupBy(p => p.CustomerReceiptId).Select(g => new { ReceiptId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.ReceiptId, g => g.Count, ct);

        var rows = receipts.Select(r => new Dictionary<string, object?>
        {
            ["receiptNumber"] = r.ReceiptNumber, ["receiptDate"] = r.ReceiptDate, ["method"] = r.PaymentMethod, ["instrumentNo"] = r.InstrumentNo,
            ["amount"] = r.ReceiptAmount, ["invoicesAllocated"] = allocationCounts.GetValueOrDefault(r.CustomerReceiptId, 0), ["status"] = r.Status
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus09Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "invoicesOpen", "balance", "overdue", "credit" };
        var today = f.AsOf ?? await clock.TodayAsync(tenant.TenantId);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(invoices.Select(i => i.CustomerId), ct);
        var rows = invoices.GroupBy(i => i.CustomerId).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = customerCodes.GetValueOrDefault(g.Key), ["invoicesOpen"] = g.Count(i => i.BalanceAmount != 0),
            ["balance"] = g.Sum(i => i.BalanceAmount),
            ["overdue"] = g.Where(i => i.DueDate != null && i.DueDate < today && i.BalanceAmount > 0).Sum(i => i.BalanceAmount),
            ["credit"] = -g.Where(i => i.BalanceAmount < 0).Sum(i => i.BalanceAmount)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Cus10Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "month", "netInvoiced" };
        var to = f.To ?? await clock.TodayAsync(tenant.TenantId);
        var from = f.From ?? to.AddMonths(-11);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted
            && i.InvoiceDate >= from && i.InvoiceDate <= to && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);

        var customerCodes = await CustomerCodesAsync(invoices.Select(i => i.CustomerId), ct);
        var rows = invoices.GroupBy(i => (i.CustomerId, Month: new DateOnly(i.InvoiceDate.Year, i.InvoiceDate.Month, 1)))
            .Select(g => new Dictionary<string, object?>
            {
                ["customerCode"] = customerCodes.GetValueOrDefault(g.Key.CustomerId), ["month"] = g.Key.Month, ["netInvoiced"] = g.Sum(i => i.NetAmount)
            }).OrderBy(r => r["month"]).ToList();
        return (columns, rows);
    }

    private async Task<Dictionary<int, string>> CustomerCodesAsync(IEnumerable<int> customerIds, CancellationToken ct)
    {
        var ids = customerIds.Distinct().ToList();
        return await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && ids.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);
    }
}
