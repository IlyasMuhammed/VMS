using VMS.Modules.Trips.Services;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-11: the pure date-range math behind §26's overlap rule, tested without a database (mirrors how
/// <c>ChargeSchedule</c>/<c>NumberFormat</c> keep their own date logic testable in isolation).</summary>
public sealed class TripRateOverlapTests
{
    private static ExistingRateRange Range(long id, string from, string? to) => new(id, DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to));

    [Fact]
    public void No_existing_rates_is_always_clear()
    {
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-07-01"), null, []);
        Assert.Equal(OverlapOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void A_range_that_does_not_touch_an_existing_one_is_clear()
    {
        var existing = new[] { Range(1, "2026-07-01", "2026-07-15") };
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-07-16"), DateOnly.Parse("2026-07-31"), existing);
        Assert.Equal(OverlapOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void AC_14_a_range_overlapping_a_finite_existing_one_is_rejected()
    {
        var existing = new[] { Range(1, "2026-07-01", "2026-07-15") };
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-07-10"), DateOnly.Parse("2026-07-20"), existing);
        Assert.Equal(OverlapOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public void Touching_boundaries_count_as_overlap_dates_are_inclusive()
    {
        var existing = new[] { Range(1, "2026-07-01", "2026-07-15") };
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-07-15"), DateOnly.Parse("2026-07-20"), existing);
        Assert.Equal(OverlapOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public void AC_56_a_later_rate_auto_closes_an_open_ended_one_instead_of_being_rejected()
    {
        var existing = new[] { Range(1, "2026-07-01", null) };
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-08-16"), null, existing);
        Assert.Equal(OverlapOutcome.AutoCloses, result.Outcome);
        Assert.Equal(1, result.CloseRateId);
        Assert.Equal(DateOnly.Parse("2026-08-15"), result.NewCloseEffectiveTo);
    }

    [Fact]
    public void A_range_starting_on_or_before_an_open_ended_rates_own_start_is_rejected_not_auto_closed()
    {
        var existing = new[] { Range(1, "2026-07-01", null) };
        Assert.Equal(OverlapOutcome.Rejected, TripRateOverlap.Check(DateOnly.Parse("2026-07-01"), null, existing).Outcome);
        Assert.Equal(OverlapOutcome.Rejected, TripRateOverlap.Check(DateOnly.Parse("2026-06-15"), DateOnly.Parse("2026-07-10"), existing).Outcome);
    }

    [Fact]
    public void Excluding_a_rate_lets_it_be_compared_against_itself_during_an_update()
    {
        var existing = new[] { Range(1, "2026-07-01", "2026-07-15") };
        var result = TripRateOverlap.Check(DateOnly.Parse("2026-07-05"), DateOnly.Parse("2026-07-20"), existing, excludingRateId: 1);
        Assert.Equal(OverlapOutcome.Clear, result.Outcome);
    }
}
