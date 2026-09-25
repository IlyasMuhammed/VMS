using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Numbering;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-09: the numbering series master and the gap-free allocator.</summary>
[Collection(ApiCollection.Name)]
public sealed class NumberSeriesTests(ApiFactory factory)
{
    private static readonly DateOnly March2026 = new(2026, 3, 1);

    // ── The printed form ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BP", 5, "Yearly", 147, "BP-26-00147")]     // the examples in FSD §24.1
    [InlineData("VH", 4, "Yearly", 32, "VH-26-0032")]
    [InlineData("VT", 6, "Yearly", 1204, "VT-26-001204")]
    [InlineData("FA", 4, "Yearly", 11, "FA-26-0011")]
    [InlineData("DOC", 6, "Yearly", 583, "DOC-26-000583")]
    [InlineData("BP", 5, "Monthly", 1, "BP-2603-00001")]
    [InlineData("BP", 5, "Never", 1, "BP-00001")]
    [InlineData("BP", 5, "Yearly", 123456, "BP-26-123456")]   // never truncated once the count outgrows the padding
    public void The_number_is_printed_the_way_the_fsd_shows_it(string prefix, int padding, string reset, long number, string expected)
    {
        Assert.Equal(expected, NumberFormat.Format(prefix, padding, reset, March2026, number));
    }

    [Fact]
    public void The_period_key_says_which_counter_a_date_belongs_to()
    {
        Assert.Equal("2026", NumberFormat.PeriodKey("Yearly", March2026));
        Assert.Equal("202603", NumberFormat.PeriodKey("Monthly", March2026));
        Assert.Equal("", NumberFormat.PeriodKey("Never", March2026));
    }

    // ── Allocation ──────────────────────────────────────────────────────────────────

    private DbContext NewDb() => new(new DbContextOptionsBuilder<DbContext>().UseSqlServer(factory.ConnectionString).Options);

    private async Task<string> NextAsync(Guid tenant, string code, DateOnly? date = null)
    {
        using var scope = factory.Services.CreateScope();
        var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
        await using var db = NewDb();
        return await db.InTransactionAsync(ct => series.NextAsync(db, code, date ?? March2026, tenant, ct));
    }

    [Fact]
    public async Task Each_series_starts_at_one_in_the_form_the_fsd_gives()
    {
        factory.CreateClient();
        var tenant = Guid.NewGuid();

        Assert.Equal("BP-26-00001", await NextAsync(tenant, NumberSeriesCodes.BusinessPartner));
        Assert.Equal("VH-26-0001", await NextAsync(tenant, NumberSeriesCodes.Vehicle));
        Assert.Equal("VT-26-000001", await NextAsync(tenant, NumberSeriesCodes.VehicleTransaction));
        Assert.Equal("FA-26-0001", await NextAsync(tenant, NumberSeriesCodes.FinanceAgreement));
        Assert.Equal("DOC-26-000001", await NextAsync(tenant, NumberSeriesCodes.Document));
        Assert.Equal("BP-26-00002", await NextAsync(tenant, NumberSeriesCodes.BusinessPartner));
        Assert.Equal("BP-26-00003", await NextAsync(tenant, NumberSeriesCodes.BusinessPartner));
    }

    [Fact]
    public async Task A_new_year_starts_again_and_the_old_year_carries_on_where_it_was()
    {
        factory.CreateClient();
        var tenant = Guid.NewGuid();
        await NextAsync(tenant, "BP", new DateOnly(2026, 12, 31));
        await NextAsync(tenant, "BP", new DateOnly(2026, 12, 31));

        Assert.Equal("BP-27-00001", await NextAsync(tenant, "BP", new DateOnly(2027, 1, 1)));
        Assert.Equal("BP-26-00003", await NextAsync(tenant, "BP", new DateOnly(2026, 12, 31)));   // a late entry for last year
    }

    [Fact]
    public async Task Tenants_number_independently()
    {
        factory.CreateClient();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await NextAsync(first, "BP");
        await NextAsync(first, "BP");

        Assert.Equal("BP-26-00001", await NextAsync(second, "BP"));
    }

    [Fact]
    public async Task A_save_that_fails_gives_its_number_back()
    {
        factory.CreateClient();
        var tenant = Guid.NewGuid();
        Assert.Equal("BP-26-00001", await NextAsync(tenant, "BP"));

        using (var scope = factory.Services.CreateScope())
        {
            var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
            await using var db = NewDb();
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.InTransactionAsync(async ct =>
            {
                Assert.Equal("BP-26-00002", await series.NextAsync(db, "BP", March2026, tenant, ct));
                Assert.Equal("BP-26-00003", await series.NextAsync(db, "BP", March2026, tenant, ct));
                throw new InvalidOperationException("The save failed after the numbers were taken.");
            }));
        }

        Assert.Equal("BP-26-00002", await NextAsync(tenant, "BP"));   // no gap: 2 was never spent
    }

    [Fact]
    public async Task Allocating_outside_a_transaction_is_refused_and_spends_nothing()
    {
        factory.CreateClient();
        var tenant = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
        await using var db = NewDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => series.NextAsync(db, "BP", March2026, tenant));

        Assert.Contains("inside the transaction", ex.Message);
        Assert.Equal("BP-26-00001", await NextAsync(tenant, "BP"));
    }

    [Fact]
    public async Task An_unknown_series_is_refused()
    {
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
        await using var db = NewDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.InTransactionAsync(ct => series.NextAsync(db, "NOPE", March2026, Guid.NewGuid(), ct)));
    }

    [Fact]
    public async Task Many_saves_at_once_never_share_a_number_and_leave_no_gap_even_when_some_fail()
    {
        factory.CreateClient();
        var tenant = Guid.NewGuid();
        const int callers = 80;   // every fourth fails, so 60 commit

        // The series row exists from last year, but this year's counter does not: every caller in the
        // burst races to start it, which is the hard case (the first save of a new year, under load).
        await NextAsync(tenant, "BP", new DateOnly(2025, 6, 1));
        var newYear = new DateOnly(2026, 1, 2);

        // Every caller opens its connection and transaction first, then all are released together, so
        // they really do collide instead of arriving one after another.
        var go = new TaskCompletionSource();
        var ready = new CountdownEvent(callers);

        var saves = Enumerable.Range(0, callers).Select(async i =>
        {
            using var scope = factory.Services.CreateScope();
            var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
            await using var db = NewDb();
            await using var transaction = await db.Database.BeginTransactionAsync();
            ready.Signal();
            await go.Task;

            var number = await series.NextAsync(db, "BP", newYear, tenant);
            await Task.Delay(Random.Shared.Next(1, 15));   // the rest of the save takes a moment

            if (i % 4 == 0)
            {
                await transaction.RollbackAsync();   // every fourth save fails
                return null;
            }
            await transaction.CommitAsync();
            return number;
        }).ToList();

        Assert.True(await Task.Run(() => ready.Wait(TimeSpan.FromSeconds(60))), "The callers did not all get a connection.");
        go.SetResult();
        var results = await Task.WhenAll(saves);

        var committed = results.Where(r => r is not null).Select(r => r!).ToList();
        Assert.Equal(60, committed.Count);
        Assert.Equal(committed.Count, committed.Distinct().Count());
        // Sixty saves committed, so exactly 1..60 were used: nothing skipped, nothing repeated.
        Assert.Equal(Enumerable.Range(1, 60).Select(n => $"BP-26-{n:00000}"), committed.Order());
        Assert.Equal("BP-26-00061", await NextAsync(tenant, "BP", newYear));
    }

    [Fact]
    public async Task Starting_a_new_period_under_load_never_repeats_a_number()
    {
        // The first save of a new year is when several callers can find no counter and all try to
        // create it. Repeat that many times: one clean round proves little, because the collision window
        // is a few microseconds wide.
        factory.CreateClient();
        var tenant = Guid.NewGuid();
        await NextAsync(tenant, "BP", new DateOnly(2025, 6, 1));   // the series row exists; each round's counter does not
        const int rounds = 40, callers = 12;

        for (var round = 0; round < rounds; round++)
        {
            var day = new DateOnly(2100 + round, 1, 2);
            var go = new TaskCompletionSource();
            var ready = new CountdownEvent(callers);

            var saves = Enumerable.Range(0, callers).Select(async _ =>
            {
                using var scope = factory.Services.CreateScope();
                var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
                await using var db = NewDb();
                await using var transaction = await db.Database.BeginTransactionAsync();
                ready.Signal();
                await go.Task;

                var number = await series.NextAsync(db, "BP", day, tenant);
                await transaction.CommitAsync();
                return number;
            }).ToList();

            Assert.True(await Task.Run(() => ready.Wait(TimeSpan.FromSeconds(60))));
            go.SetResult();
            var numbers = await Task.WhenAll(saves);

            var yy = day.ToString("yy");
            Assert.Equal(Enumerable.Range(1, callers).Select(n => $"BP-{yy}-{n:00000}"), numbers.Order());
        }
    }

    // ── The series master ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_master_lists_every_series_with_the_number_the_next_record_will_get()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var list = await (await admin.GetAsync("/api/admin/number-series")).DataAsync();

        var codes = list.EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        Assert.Equal(["BP", "VH", "VT", "FA", "DOC", "CUS"], codes);
        var bp = list.EnumerateArray().First(s => s.GetProperty("code").GetString() == "BP");
        Assert.Equal("Business Partner", bp.GetProperty("entity").GetString());
        Assert.Equal(5, bp.GetProperty("padding").GetInt32());
        Assert.Equal("Yearly", bp.GetProperty("resetPeriod").GetString());
        Assert.Matches(@"^BP-\d\d-\d{5}$", bp.GetProperty("nextNumber").GetString()!);
    }

    [Fact]
    public async Task Changing_a_series_applies_from_the_next_number_and_is_audited()
    {
        using var admin = await factory.AsSuperAdminAsync();
        await admin.GetAsync("/api/admin/number-series");   // creates the platform tenant's rows

        var response = await admin.PutAsJsonAsync("/api/admin/number-series/DOC", new { prefix = "dm", padding = 8, resetPeriod = "Never" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.DataAsync();
        Assert.Equal("DM", updated.GetProperty("prefix").GetString());      // normalised to upper case
        Assert.Equal("Never", updated.GetProperty("resetPeriod").GetString());
        Assert.Matches(@"^DM-\d{8}$", updated.GetProperty("nextNumber").GetString()!);

        var audit = factory.Query(
            "SELECT Field, OldValue, NewValue FROM core.AuditEntries WHERE Entity = 'NumberSeries' AND Action = 'Updated' AND Field = 'Prefix' ORDER BY AuditEntryID DESC");
        Assert.Equal("DOC", audit[0]["OldValue"]);
        Assert.Equal("DM", audit[0]["NewValue"]);
    }

    [Theory]
    [InlineData("", 5, "Yearly")]           // no prefix
    [InlineData("bad prefix", 5, "Yearly")] // not letters or digits
    [InlineData("WAYTOOLONGPREFIX", 5, "Yearly")]
    [InlineData("BP", 2, "Yearly")]         // too few digits
    [InlineData("BP", 11, "Yearly")]        // too many
    [InlineData("BP", 5, "Weekly")]         // not a reset period
    public async Task An_invalid_series_is_rejected_and_nothing_changes(string prefix, int padding, string reset)
    {
        using var admin = await factory.AsSuperAdminAsync();
        await admin.GetAsync("/api/admin/number-series");

        var response = await admin.PutAsJsonAsync("/api/admin/number-series/BP", new { prefix, padding, resetPeriod = reset });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var bp = (await (await admin.GetAsync("/api/admin/number-series")).DataAsync()).EnumerateArray()
            .First(s => s.GetProperty("code").GetString() == "BP");
        Assert.Equal("BP", bp.GetProperty("prefix").GetString());
    }

    [Fact]
    public async Task An_unknown_series_is_not_found()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var response = await admin.PutAsJsonAsync("/api/admin/number-series/NOPE", new { prefix = "X", padding = 5, resetPeriod = "Yearly" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_a_user_who_may_manage_series_can_see_or_change_them()
    {
        using var reader = factory.CreateClient().WithToken(TestTokens.For("Hana Lee", 9301, PermissionCodes.USER_VIEW));
        using var manager = factory.CreateClient().WithToken(TestTokens.For("Ivo Marsh", 9302, PermissionCodes.ADM_SERIES_MANAGE));

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/admin/number-series")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reader.PutAsJsonAsync("/api/admin/number-series/BP", new { prefix = "X", padding = 5, resetPeriod = "Yearly" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/admin/number-series")).StatusCode);
    }
}
