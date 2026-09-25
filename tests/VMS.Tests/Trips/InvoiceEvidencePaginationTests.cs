using VMS.Modules.Trips.Services;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-28: §41's own pagination rules, tested without a database (mirrors how <c>TripRateOverlap</c>/
/// <c>TripLifecycle</c> keep their own pure business logic testable in isolation).</summary>
public sealed class InvoiceEvidencePaginationTests
{
    private static InvoiceEvidencePagination.LineInput Line(string reg, string date, string tripNo, decimal amount = 1000) =>
        new(reg, DateOnly.Parse(date), tripNo, "LHR -> FSD", null, amount);

    [Fact]
    public void No_lines_is_zero_pages()
    {
        var result = InvoiceEvidencePagination.Paginate([], 50);
        Assert.Equal(0, result.PageCount);
        Assert.Equal(0, result.LineCount);
        Assert.Empty(result.VehiclePages);
    }

    [Fact]
    public void One_vehicle_under_the_page_size_is_one_page()
    {
        var lines = new[] { Line("ABC-123", "2026-07-01", "T1"), Line("ABC-123", "2026-07-02", "T2") };
        var result = InvoiceEvidencePagination.Paginate(lines, 50);
        Assert.Equal(1, result.PageCount);
        var vehicle = Assert.Single(result.VehiclePages);
        Assert.Equal(1, vehicle.FirstPage);
        Assert.Equal(1, vehicle.LastPage);
        Assert.Equal(2, vehicle.LineCount);
    }

    [Fact]
    public void AC_33_a_vehicle_over_the_page_size_continues_on_the_next_page_under_its_own_header()
    {
        // "ABC-123 Page 1 rows 1-50, ABC-123 Page 2 rows 51-100 (same vehicle continues)" — reproduced at a
        // pageSize of 2 instead of the FSD's own worked example's 50, since the rule is scale-independent.
        var lines = new[]
        {
            Line("ABC-123", "2026-07-01", "T1"), Line("ABC-123", "2026-07-02", "T2"), Line("ABC-123", "2026-07-03", "T3")
        };
        var result = InvoiceEvidencePagination.Paginate(lines, 2);
        Assert.Equal(2, result.PageCount);
        var vehicle = Assert.Single(result.VehiclePages);
        Assert.Equal(1, vehicle.FirstPage);
        Assert.Equal(2, vehicle.LastPage);
        Assert.Equal(2, result.Pages[0].Lines.Count);
        Assert.Single(result.Pages[1].Lines);
    }

    [Fact]
    public void AC_33_a_new_vehicle_always_starts_a_fresh_page_even_with_room_left()
    {
        // "ABC-456 Page 3 rows 1-37 (new vehicle, new page)" — never shares a page with ABC-123's own tail,
        // even though ABC-123's second page (pageSize 50) has plenty of room left after only 1 line.
        var lines = new[]
        {
            Line("ABC-123", "2026-07-01", "T1"), Line("ABC-123", "2026-07-02", "T2"), Line("ABC-123", "2026-07-03", "T3"),
            Line("ABC-456", "2026-07-10", "T4")
        };
        var result = InvoiceEvidencePagination.Paginate(lines, 2);
        Assert.Equal(3, result.PageCount);
        var abc123 = result.VehiclePages.Single(v => v.VehicleRegNo == "ABC-123");
        var abc456 = result.VehiclePages.Single(v => v.VehicleRegNo == "ABC-456");
        Assert.Equal(1, abc123.FirstPage);
        Assert.Equal(2, abc123.LastPage);
        Assert.Equal(3, abc456.FirstPage);
        Assert.Equal(3, abc456.LastPage);
        Assert.Single(result.Pages[2].Lines);
    }

    [Fact]
    public void Lines_are_ordered_by_vehicle_then_date_then_trip_number_regardless_of_input_order()
    {
        var lines = new[]
        {
            Line("ABC-456", "2026-07-10", "T4"), Line("ABC-123", "2026-07-03", "T3"),
            Line("ABC-123", "2026-07-01", "T1"), Line("ABC-123", "2026-07-02", "T2")
        };
        var result = InvoiceEvidencePagination.Paginate(lines, 50);
        var abc123Page = result.Pages.Single(p => p.VehicleRegNo == "ABC-123");
        Assert.Equal(["T1", "T2", "T3"], abc123Page.Lines.Select(l => l.TripNumber));
    }

    [Fact]
    public void Vehicle_subtotal_sums_only_that_vehicles_own_lines()
    {
        var lines = new[] { Line("ABC-123", "2026-07-01", "T1", 1000), Line("ABC-123", "2026-07-02", "T2", 2000), Line("ABC-456", "2026-07-03", "T3", 5000) };
        var result = InvoiceEvidencePagination.Paginate(lines, 50);
        Assert.Equal(3000, result.VehiclePages.Single(v => v.VehicleRegNo == "ABC-123").Subtotal);
        Assert.Equal(5000, result.VehiclePages.Single(v => v.VehicleRegNo == "ABC-456").Subtotal);
    }
}
