using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Pagination;
using VMS.Shared.Time;
using ClosedXML.Excel;

namespace VMS.Modules.Vehicles.Services;

public interface IVehicleQueryService
{
    Task<PaginatedResponse<VehicleListItem>> ListAsync(VehicleListQuery query);
    Task<(byte[] Content, string FileName)> ExportAsync(VehicleListQuery query);
}

/// <summary>The vehicle list (FSD §21): search by registration, chassis, engine or code, filters, server paging and sorting.</summary>
internal sealed class VehicleQueryService(VehicleContext ctx, ILookupReader lookups, ICallerScope scope, IClientTimeZone clientZone, TimeProvider time) : IVehicleQueryService
{
    public const int MinSearchLength = 3;
    public const int MaxPageSize = 100;

    public async Task<PaginatedResponse<VehicleListItem>> ListAsync(VehicleListQuery q)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize <= 0 ? 25 : q.PageSize, 1, MaxPageSize);

        var query = Filter(q);
        var total = await query.CountAsync();
        var rows = await Sort(query, q.Sort).Skip((page - 1) * size).Take(size).ToListAsync();

        var types = await lookups.FindManyAsync(PlatformLookups.VehicleType, rows.Select(r => r.VehicleTypeId));
        var makes = await lookups.FindManyAsync(PlatformLookups.Make, rows.Select(r => r.MakeId));
        var refs = await ctx.RefsAsync(rows.SelectMany(r => new int?[] { r.CurrentCounterpartyId, r.DefaultDriverId }));
        var branches = await ctx.Branches.FindManyAsync(rows.Where(r => r.BranchId is not null).Select(r => r.BranchId!.Value));

        var items = rows.Select(v => new VehicleListItem
        {
            Id = v.VehicleId, VehicleCode = v.VehicleCode, RegistrationNo = v.RegistrationNo, VehicleTypeId = v.VehicleTypeId, VehicleType = types.GetValueOrDefault(v.VehicleTypeId)?.Description,
            MakeId = v.MakeId, Make = makes.GetValueOrDefault(v.MakeId)?.Description, Model = v.Model, CurrentCategory = v.CurrentCategory,
            Counterparty = VehicleContext.Ref(refs, v.CurrentCounterpartyId), BranchId = v.BranchId, Branch = v.BranchId is { } b ? branches.GetValueOrDefault(b)?.Name : null,
            Status = v.Status, Driver = VehicleContext.Ref(refs, v.DefaultDriverId), ModifiedOn = v.ModifiedOn
        }).ToList();
        return new PaginatedResponse<VehicleListItem> { Items = items, TotalCount = total, Page = page, PageSize = size };
    }

    public const int MaxExportRows = 50_000;

    /// <summary>The filtered list as an Excel file: every vehicle the filter matches, not one page. Times are in the exporting person's own zone (NFR-DT-07). The columns hold nothing a caller needs a field permission for.</summary>
    public async Task<(byte[] Content, string FileName)> ExportAsync(VehicleListQuery q)
    {
        var query = Filter(q);
        var total = await query.CountAsync();
        if (total > MaxExportRows) throw new ValidationException(ctx.Messages.Error("", Msg.ExportTooLarge, ("n", total), ("Max", MaxExportRows)));

        var rows = await Sort(query, q.Sort).ToListAsync();
        var types = await lookups.FindManyAsync(PlatformLookups.VehicleType, rows.Select(r => r.VehicleTypeId).Distinct());
        var makes = await lookups.FindManyAsync(PlatformLookups.Make, rows.Select(r => r.MakeId).Distinct());
        var refs = await ctx.RefsAsync(rows.SelectMany(r => new int?[] { r.CurrentCounterpartyId, r.DefaultDriverId }));
        var branches = await ctx.Branches.FindManyAsync(rows.Where(r => r.BranchId is not null).Select(r => r.BranchId!.Value).Distinct());
        var zone = clientZone.Current ?? TimeZoneInfo.Utc;

        string[] headers = ["Vehicle code", "Registration", "Type", "Make", "Model", "Chassis", "Engine", "Fuel", "Category", "Counterparty", "Branch", "Status", "Driver", $"Last modified ({zone.Id})"];
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Vehicles");
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Row(1).Style.Font.Bold = true;

        for (var r = 0; r < rows.Count; r++)
        {
            var v = rows[r];
            string?[] cells =
            [
                v.VehicleCode, v.RegistrationNo, types.GetValueOrDefault(v.VehicleTypeId)?.Description, makes.GetValueOrDefault(v.MakeId)?.Description, v.Model, v.ChassisNo, v.EngineNo, v.FuelType,
                v.CurrentCategory, VehicleContext.Ref(refs, v.CurrentCounterpartyId)?.Name, v.BranchId is { } vb ? branches.GetValueOrDefault(vb)?.Name : null, v.Status,
                VehicleContext.Ref(refs, v.DefaultDriverId)?.Name
            ];
            for (var c = 0; c < cells.Length; c++)
            {
                // Text a person typed must never be read by the spreadsheet as a formula.
                var cell = sheet.Cell(r + 2, c + 1);
                cell.SetValue(cells[c] ?? string.Empty);
                cell.Style.NumberFormat.Format = "@";
            }
            var modified = sheet.Cell(r + 2, cells.Length + 1);
            modified.Value = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v.ModifiedOn, DateTimeKind.Utc), zone);
            modified.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"vehicles-{TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone):yyyyMMdd-HHmm}.xlsx");
    }
    /// <summary>The vehicles the query matches, before paging. A search shorter than three characters is refused.</summary>
    private IQueryable<Vehicle> Filter(VehicleListQuery q)
    {
        var query = ApplyScope(ctx.Own().AsNoTracking());
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            if (term.Length < MinSearchLength)
                throw new ValidationException(ctx.Messages.Error("search", Msg.MinLength, ("Field", "Search"), ("Min", MinSearchLength)));
            var key = RegistrationNumber.Key(term);   // "les 12" finds LES-1234
            var upper = term.ToUpperInvariant();
            query = query.Where(v => v.RegNoKey.Contains(key) || v.VehicleCode.Contains(upper) || (v.ChassisNo != null && v.ChassisNo.Contains(upper)) || (v.EngineNo != null && v.EngineNo.Contains(upper)));
        }
        if (q.Category is { Count: > 0 }) { var categories = q.Category; query = query.Where(v => v.CurrentCategory != null && categories.Contains(v.CurrentCategory)); }
        if (q.Status is { Count: > 0 }) { var statuses = q.Status; query = query.Where(v => statuses.Contains(v.Status)); }
        if (q.VehicleTypeId is { } type) query = query.Where(v => v.VehicleTypeId == type);
        if (q.MakeId is { } make) query = query.Where(v => v.MakeId == make);
        if (q.BranchId is { } branch) query = query.Where(v => v.BranchId == branch);
        if (q.DriverId is { } driver) query = query.Where(v => v.DefaultDriverId == driver);
        return query;
    }

    /// <summary>§23B.4: Own branch filters to the caller's branch; Own vehicles — the driver app's scope — to vehicles where the caller's linked partner is the assigned driver; Own records to vehicles the caller themselves created.</summary>
    private IQueryable<Vehicle> ApplyScope(IQueryable<Vehicle> query) => scope.ScopeType switch
    {
        ScopeTypes.OwnBranch => query.Where(v => v.BranchId == scope.BranchId),
        ScopeTypes.OwnVehicles => query.Where(v => scope.LinkedPartnerId != null && v.DefaultDriverId == scope.LinkedPartnerId),
        ScopeTypes.OwnRecords => query.Where(v => v.CreatedBy == scope.UserId),
        _ => query,
    };

    private static IQueryable<Vehicle> Sort(IQueryable<Vehicle> q, string? sort)
    {
        var parts = (sort ?? "modifiedOn,desc").Split(',', 2, StringSplitOptions.TrimEntries);
        var descending = parts.Length < 2 ? parts[0].Equals("modifiedOn", StringComparison.OrdinalIgnoreCase) : !parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);

        // Only these: a client cannot sort on a column that is not indexed.
        return parts[0].ToLowerInvariant() switch
        {
            "registrationno" => descending ? q.OrderByDescending(v => v.RegNoKey) : q.OrderBy(v => v.RegNoKey),
            "vehiclecode" => descending ? q.OrderByDescending(v => v.VehicleCode) : q.OrderBy(v => v.VehicleCode),
            "status" => descending ? q.OrderByDescending(v => v.Status).ThenBy(v => v.RegNoKey) : q.OrderBy(v => v.Status).ThenBy(v => v.RegNoKey),
            "category" => descending ? q.OrderByDescending(v => v.CurrentCategory).ThenBy(v => v.RegNoKey) : q.OrderBy(v => v.CurrentCategory).ThenBy(v => v.RegNoKey),
            _ => descending ? q.OrderByDescending(v => v.ModifiedOn).ThenByDescending(v => v.VehicleId) : q.OrderBy(v => v.ModifiedOn).ThenBy(v => v.VehicleId)
        };
    }
}
