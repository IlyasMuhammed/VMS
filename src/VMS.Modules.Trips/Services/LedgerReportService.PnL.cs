using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Lookups;
// ReportFilter/TripPnLQuery/TripPnLModel all live in VMS.Modules.Trips.Models, already imported above.

namespace VMS.Modules.Trips.Services;

/// <summary>§42.4: PNL-01..13. PNL-08's own literal doc comment (<see cref="TripPnLSummaryModel"/>) names this
/// exact task as the reader of <see cref="ITripPnLService.SummaryAsync"/>'s underlying calculation — PNL-08..13
/// all read it once and group the same already-computed lines by vehicle/customer/route/driver/month, rather than
/// re-deriving the §31 formula five more times.</summary>
internal sealed partial class LedgerReportService
{
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "expenseDate", "type", "otherType", "amount", "method", "vendor", "approvalStatus" };
        var expenses = await db.TripExpenses.AsNoTracking().Where(e => e.TenantId == tenant.TenantId
            && (f.From == null || DateOnly.FromDateTime(e.ExpenseDate) >= f.From) && (f.To == null || DateOnly.FromDateTime(e.ExpenseDate) <= f.To)
            && (string.IsNullOrEmpty(f.Status) || e.ApprovalStatus == f.Status)).ToListAsync(ct);
        if (expenses.Count == 0) return (columns, []);

        var tripIds = expenses.Select(e => e.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var types = await lookups.FindManyAsync(PlatformLookups.TripExpenseType, expenses.Select(e => e.ExpenseTypeId).Distinct());
        var vendorIds = expenses.Where(e => e.BusinessPartnerId != null).Select(e => e.BusinessPartnerId!.Value).Distinct();
        var vendors = await partners.FindManyAsync(vendorIds, ct);

        var rows = expenses.Select(e => new Dictionary<string, object?>
        {
            ["tripNumber"] = tripNumbers.GetValueOrDefault(e.TripId), ["expenseDate"] = e.ExpenseDate, ["type"] = types.GetValueOrDefault(e.ExpenseTypeId)?.Description,
            ["otherType"] = e.OtherExpenseType, ["amount"] = e.Amount, ["method"] = e.PaymentMethod,
            ["vendor"] = e.BusinessPartnerId is { } v ? vendors.GetValueOrDefault(v)?.DisplayName : null, ["approvalStatus"] = e.ApprovalStatus
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "type", "month", "totalAmount" };
        var expenses = await db.TripExpenses.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && !e.IsVoided
            && (f.From == null || DateOnly.FromDateTime(e.ExpenseDate) >= f.From) && (f.To == null || DateOnly.FromDateTime(e.ExpenseDate) <= f.To)).ToListAsync(ct);
        if (f.VehicleId is { } vehicleId)
        {
            var tripIdsForVehicle = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.VehicleId == vehicleId).Select(t => t.TripId).ToListAsync(ct);
            expenses = expenses.Where(e => tripIdsForVehicle.Contains(e.TripId)).ToList();
        }
        if (expenses.Count == 0) return (columns, []);

        var types = await lookups.FindManyAsync(PlatformLookups.TripExpenseType, expenses.Select(e => e.ExpenseTypeId).Distinct());
        var rows = expenses.GroupBy(e => (e.ExpenseTypeId, Month: new DateOnly(e.ExpenseDate.Year, e.ExpenseDate.Month, 1)))
            .Select(g => new Dictionary<string, object?> { ["type"] = types.GetValueOrDefault(g.Key.ExpenseTypeId)?.Description, ["month"] = g.Key.Month, ["totalAmount"] = g.Sum(e => e.Amount) })
            .OrderBy(r => r["month"]).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "fuelDateTime", "vehicleRegNo", "fuelType", "quantity", "rate", "amount", "method", "card" };
        var fuels = await db.TripFuels.AsNoTracking().Where(x => x.TenantId == tenant.TenantId
            && (f.From == null || DateOnly.FromDateTime(x.FuelDateTime) >= f.From) && (f.To == null || DateOnly.FromDateTime(x.FuelDateTime) <= f.To)
            && (f.VehicleId == null || x.VehicleId == f.VehicleId) && (string.IsNullOrEmpty(f.Method) || x.PaymentMethod == f.Method)).ToListAsync(ct);
        if (fuels.Count == 0) return (columns, []);

        var tripIds = fuels.Select(x => x.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var vehicleInfos = await vehicles.FindManyAsync(fuels.Select(x => x.VehicleId).Distinct(), ct);
        var cardIds = fuels.Where(x => x.FuelCardId != null).Select(x => x.FuelCardId!.Value).Distinct().ToList();
        var cards = cardIds.Count == 0 ? new Dictionary<int, string>() : await db.FuelCards.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && cardIds.Contains(c.FuelCardId)).ToDictionaryAsync(c => c.FuelCardId, c => Mask(c.CardNumber), ct);

        var rows = fuels.Select(x => new Dictionary<string, object?>
        {
            ["tripNumber"] = tripNumbers.GetValueOrDefault(x.TripId), ["fuelDateTime"] = x.FuelDateTime, ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(x.VehicleId)?.RegistrationNo,
            ["fuelType"] = x.FuelType, ["quantity"] = x.Quantity, ["rate"] = x.Rate, ["amount"] = x.Amount, ["method"] = x.PaymentMethod,
            ["card"] = x.FuelCardId is { } cid ? cards.GetValueOrDefault(cid) : null
        }).ToList();
        return (columns, rows);
    }

    private static string Mask(string cardNumber) => cardNumber.Length <= 4 ? cardNumber : new string('*', cardNumber.Length - 4) + cardNumber[^4..];

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "vehicleRegNo", "totalKm", "totalLitres", "kmPerLitre", "costPerKm" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.StartOdometer != null && t.EndOdometer != null
            && (f.From == null || t.TripDate >= f.From) && (f.To == null || t.TripDate <= f.To) && (f.VehicleId == null || t.VehicleId == f.VehicleId)).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);

        var tripIds = trips.Select(t => t.TripId).ToList();
        var fuelByTrip = await db.TripFuels.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && !x.IsVoided && tripIds.Contains(x.TripId))
            .GroupBy(x => x.TripId).Select(g => new { TripId = g.Key, Litres = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(g => g.TripId, ct);
        var vehicleInfos = await vehicles.FindManyAsync(trips.Select(t => t.VehicleId).Distinct(), ct);

        var rows = trips.GroupBy(t => t.VehicleId).Select(g =>
        {
            var km = g.Sum(t => t.EndOdometer!.Value - t.StartOdometer!.Value);
            var litres = g.Sum(t => fuelByTrip.GetValueOrDefault(t.TripId)?.Litres ?? 0);
            var amount = g.Sum(t => fuelByTrip.GetValueOrDefault(t.TripId)?.Amount ?? 0);
            return new Dictionary<string, object?>
            {
                ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(g.Key)?.RegistrationNo, ["totalKm"] = km, ["totalLitres"] = litres,
                ["kmPerLitre"] = litres == 0 ? null : (decimal?)Math.Round(km / litres, 2), ["costPerKm"] = km == 0 ? null : (decimal?)Math.Round(amount / km, 2)
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "card", "vehicleRegNo", "driverName", "fills", "litres", "amount", "expiryDate" };
        var fuels = await db.TripFuels.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.FuelCardId != null
            && (f.From == null || DateOnly.FromDateTime(x.FuelDateTime) >= f.From) && (f.To == null || DateOnly.FromDateTime(x.FuelDateTime) <= f.To)).ToListAsync(ct);
        if (fuels.Count == 0) return (columns, []);

        var cardIds = fuels.Select(x => x.FuelCardId!.Value).Distinct().ToList();
        var cards = await db.FuelCards.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cardIds.Contains(c.FuelCardId)).ToDictionaryAsync(c => c.FuelCardId, ct);
        var vehicleIds = cards.Values.Where(c => c.VehicleId != null).Select(c => c.VehicleId!.Value).Distinct();
        var vehicleInfos = await vehicles.FindManyAsync(vehicleIds, ct);
        var driverIds = cards.Values.Where(c => c.DriverId != null).Select(c => c.DriverId!.Value).Distinct();
        var driverInfos = await partners.FindManyAsync(driverIds, ct);

        var rows = fuels.GroupBy(x => x.FuelCardId!.Value).Select(g =>
        {
            var card = cards.GetValueOrDefault(g.Key);
            return new Dictionary<string, object?>
            {
                ["card"] = card is null ? null : Mask(card.CardNumber), ["vehicleRegNo"] = card?.VehicleId is { } v ? vehicleInfos.GetValueOrDefault(v)?.RegistrationNo : null,
                ["driverName"] = card?.DriverId is { } d ? driverInfos.GetValueOrDefault(d)?.DisplayName : null, ["fills"] = g.Count(),
                ["litres"] = g.Sum(x => x.Quantity), ["amount"] = g.Sum(x => x.Amount), ["expiryDate"] = card?.ExpiryDate
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl06Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "card", "vehicleRegNo", "expiryDate", "status" };
        var today = await clock.TodayAsync(tenant.TenantId);
        var cutoff = f.To ?? today.AddDays(30);
        var cards = await db.FuelCards.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.ExpiryDate >= today && c.ExpiryDate <= cutoff).ToListAsync(ct);
        if (cards.Count == 0) return (columns, []);
        var vehicleIds = cards.Where(c => c.VehicleId != null).Select(c => c.VehicleId!.Value).Distinct();
        var vehicleInfos = await vehicles.FindManyAsync(vehicleIds, ct);

        var rows = cards.Select(c => new Dictionary<string, object?>
        { ["card"] = Mask(c.CardNumber), ["vehicleRegNo"] = c.VehicleId is { } v ? vehicleInfos.GetValueOrDefault(v)?.RegistrationNo : null, ["expiryDate"] = c.ExpiryDate, ["status"] = c.Status }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl07Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "customerCode", "type", "amount", "billable" };
        var incomes = await db.TripIncomes.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && !i.IsVoided
            && (f.From == null || i.IncomeDate >= f.From) && (f.To == null || i.IncomeDate <= f.To)).ToListAsync(ct);
        if (incomes.Count == 0) return (columns, []);

        var tripIds = incomes.Select(i => i.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var codes = await CustomerCodesAsync(incomes.Select(i => i.CustomerId), ct);
        var types = await lookups.FindManyAsync(PlatformLookups.TripIncomeType, incomes.Select(i => i.IncomeTypeId).Distinct());

        var rows = incomes.Select(i => new Dictionary<string, object?>
        {
            ["tripNumber"] = tripNumbers.GetValueOrDefault(i.TripId), ["customerCode"] = codes.GetValueOrDefault(i.CustomerId),
            ["type"] = types.GetValueOrDefault(i.IncomeTypeId)?.Description, ["amount"] = i.Amount, ["billable"] = i.IsBillable
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string> Columns, List<TripPnLModel> Lines, Dictionary<long, Trip> Trips)> PricedLinesAsync(ReportFilter f, CancellationToken ct)
    {
        var summary = await pnl.SummaryAsync(new TripPnLQuery { CustomerId = f.CustomerId, VehicleId = f.VehicleId, FromDate = f.From, ToDate = f.To }, ct);
        var lines = summary.Trips.Where(l => l.IsPriced).ToList();
        var tripIds = lines.Select(l => l.TripId).ToList();
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, ct);
        return ([], lines, trips);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl08Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "revenue", "income", "fuel", "expenses", "pnl", "marginPercent" };
        var (_, lines, _) = await PricedLinesAsync(f, ct);
        var rows = lines.Select(l => new Dictionary<string, object?>
        {
            ["tripNumber"] = l.TripNumber, ["revenue"] = l.Revenue, ["income"] = l.ApprovedIncome, ["fuel"] = l.Fuel, ["expenses"] = l.ApprovedExpenses,
            ["pnl"] = l.OperationalPnL, ["marginPercent"] = l.Revenue is > 0 ? Math.Round((l.OperationalPnL ?? 0) / l.Revenue.Value * 100, 1) : null
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl09Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "vehicleRegNo", "trips", "revenue", "fuel", "expenses", "pnl", "pnlPerTrip" };
        var (_, lines, trips) = await PricedLinesAsync(f, ct);
        if (lines.Count == 0) return (columns, []);
        var vehicleInfos = await vehicles.FindManyAsync(trips.Values.Select(t => t.VehicleId).Distinct(), ct);

        var rows = lines.GroupBy(l => trips[l.TripId].VehicleId).Select(g => new Dictionary<string, object?>
        {
            ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(g.Key)?.RegistrationNo, ["trips"] = g.Count(), ["revenue"] = g.Sum(l => l.Revenue ?? 0),
            ["fuel"] = g.Sum(l => l.Fuel), ["expenses"] = g.Sum(l => l.ApprovedExpenses), ["pnl"] = g.Sum(l => l.OperationalPnL ?? 0),
            ["pnlPerTrip"] = Math.Round(g.Sum(l => l.OperationalPnL ?? 0) / g.Count(), 2)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl10Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "trips", "revenue", "costs", "pnl" };
        var (_, lines, trips) = await PricedLinesAsync(f, ct);
        if (lines.Count == 0) return (columns, []);
        var codes = await CustomerCodesAsync(trips.Values.Select(t => t.CustomerId).Distinct(), ct);

        var rows = lines.GroupBy(l => trips[l.TripId].CustomerId).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = codes.GetValueOrDefault(g.Key), ["trips"] = g.Count(), ["revenue"] = g.Sum(l => l.Revenue ?? 0),
            ["costs"] = g.Sum(l => l.Fuel + l.ApprovedExpenses), ["pnl"] = g.Sum(l => l.OperationalPnL ?? 0)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl11Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "routeOrConfig", "trips", "avgRevenue", "avgCost", "pnl" };
        var (_, lines, trips) = await PricedLinesAsync(f, ct);
        if (lines.Count == 0) return (columns, []);
        var configIds = trips.Values.Where(t => t.TripConfigurationId != null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configCodes = configIds.Count == 0 ? new Dictionary<long, string>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);

        var rows = lines.GroupBy(l => trips[l.TripId].TripConfigurationId?.ToString() ?? "Open").Select(g =>
        {
            var first = trips[g.First().TripId];
            return new Dictionary<string, object?>
            {
                ["routeOrConfig"] = first.TripConfigurationId is { } id ? configCodes.GetValueOrDefault(id) : "Open trips", ["trips"] = g.Count(),
                ["avgRevenue"] = Math.Round(g.Average(l => l.Revenue ?? 0), 2), ["avgCost"] = Math.Round(g.Average(l => l.Fuel + l.ApprovedExpenses), 2), ["pnl"] = g.Sum(l => l.OperationalPnL ?? 0)
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl12Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "driverName", "trips", "driverExpenses", "fuel", "pnlOfTrips" };
        var (_, lines, trips) = await PricedLinesAsync(f, ct);
        var driverTrips = trips.Values.Where(t => t.DriverId != null && (f.DriverId == null || t.DriverId == f.DriverId)).ToList();
        if (driverTrips.Count == 0) return (columns, []);
        var driverInfos = await partners.FindManyAsync(driverTrips.Select(t => t.DriverId!.Value).Distinct(), ct);
        var tripIds = driverTrips.Select(t => t.TripId).ToList();
        // §29's own "PaidByDriver" payment method is the closest honest reading of "driver-recorded expenses."
        var driverExpenses = await db.TripExpenses.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && !e.IsVoided && tripIds.Contains(e.TripId) && e.PaymentMethod == TripExpensePaymentMethods.PaidByDriver)
            .GroupBy(e => e.TripId).Select(g => new { TripId = g.Key, Amount = g.Sum(e => e.Amount) }).ToDictionaryAsync(g => g.TripId, g => g.Amount, ct);

        var rows = driverTrips.GroupBy(t => t.DriverId!.Value).Select(g =>
        {
            var tripIdsForDriver = g.Select(t => t.TripId).ToHashSet();
            var linesForDriver = lines.Where(l => tripIdsForDriver.Contains(l.TripId)).ToList();
            return new Dictionary<string, object?>
            {
                ["driverName"] = driverInfos.GetValueOrDefault(g.Key)?.DisplayName, ["trips"] = g.Count(),
                ["driverExpenses"] = g.Sum(t => driverExpenses.GetValueOrDefault(t.TripId, 0)), ["fuel"] = linesForDriver.Sum(l => l.Fuel),
                ["pnlOfTrips"] = linesForDriver.Sum(l => l.OperationalPnL ?? 0)
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Pnl13Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "month", "revenue", "fuel", "expenses", "pnl" };
        var to = f.To ?? await clock.TodayAsync(tenant.TenantId);
        var from = f.From ?? to.AddMonths(-11);
        var (_, lines, trips) = await PricedLinesAsync(new ReportFilter { From = from, To = to }, ct);
        if (lines.Count == 0) return (columns, []);

        var rows = lines.GroupBy(l => new DateOnly(trips[l.TripId].TripDate.Year, trips[l.TripId].TripDate.Month, 1)).Select(g => new Dictionary<string, object?>
        {
            ["month"] = g.Key, ["revenue"] = g.Sum(l => l.Revenue ?? 0), ["fuel"] = g.Sum(l => l.Fuel), ["expenses"] = g.Sum(l => l.ApprovedExpenses), ["pnl"] = g.Sum(l => l.OperationalPnL ?? 0)
        }).OrderBy(r => r["month"]).ToList();
        return (columns, rows);
    }
}
