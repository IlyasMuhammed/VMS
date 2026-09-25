namespace VMS.Modules.Trips.Services;

/// <summary>§41's pagination rules, a pure, DB-free-testable function — the same "public for testability"
/// exception this module already uses for <c>TripRateOverlap.Check</c>/<c>TripLifecycle</c>/
/// <c>TripInvoiceEligibility.BlockingReasons</c> (the last kept <c>internal</c> only because it takes the
/// internal <c>Trip</c> entity directly; this one takes plain values, so it can be <c>public</c> and reached
/// from the test project with no <c>InternalsVisibleTo</c> at all).</summary>
public static class InvoiceEvidencePagination
{
    public sealed record LineInput(string VehicleRegNo, DateOnly? TripDate, string? TripNumber, string RouteLabel, string? CustomerTripReference, decimal Amount);

    /// <summary>One vehicle's own span of pages — "ABC-123 Page 1, Page 2 (same vehicle continues)."</summary>
    public sealed record VehiclePage(string VehicleRegNo, int FirstPage, int LastPage, int LineCount, decimal Subtotal);

    /// <summary>One page's own chunk of already-ordered lines, ready to render as-is.</summary>
    public sealed record Page(int PageNumber, string VehicleRegNo, IReadOnlyList<LineInput> Lines);

    public sealed record Result(IReadOnlyList<Page> Pages, IReadOnlyList<VehiclePage> VehiclePages, int PageCount, int LineCount);

    /// <summary>Rule 1: grouped by Vehicle (registration order), then Trip Date, then Trip Number. Rule 2: each
    /// vehicle starts a new page, even if the previous page had room. Rules 3-4: at most <paramref name="pageSize"/>
    /// lines per page; a vehicle with more lines continues under its own header on the next page rather than
    /// spilling into the next vehicle's page.</summary>
    public static Result Paginate(IReadOnlyList<LineInput> lines, int pageSize)
    {
        if (pageSize < 1) pageSize = 50;
        var ordered = lines
            .OrderBy(l => l.VehicleRegNo, StringComparer.Ordinal)
            .ThenBy(l => l.TripDate)
            .ThenBy(l => l.TripNumber, StringComparer.Ordinal)
            .ToList();

        var pages = new List<Page>();
        var vehiclePages = new List<VehiclePage>();
        var pageNo = 0;

        foreach (var group in ordered.GroupBy(l => l.VehicleRegNo, StringComparer.Ordinal))
        {
            var groupLines = group.ToList();
            var firstPageOfVehicle = pageNo + 1;
            for (var i = 0; i < groupLines.Count; i += pageSize)
            {
                pageNo++;
                pages.Add(new Page(pageNo, group.Key, groupLines.Skip(i).Take(pageSize).ToList()));
            }
            vehiclePages.Add(new VehiclePage(group.Key, firstPageOfVehicle, pageNo, groupLines.Count, groupLines.Sum(l => l.Amount)));
        }

        return new Result(pages, vehiclePages, pageNo, ordered.Count);
    }
}
