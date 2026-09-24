using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VMS.Shared.Common;
using VMS.Shared.Numbering;
using VMS.Shared.Time;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-11: instants are UTC everywhere, business dates carry no time zone, and the API says so unambiguously.</summary>
[Collection(ApiCollection.Name)]
public sealed class DateTimeContractTests(ApiFactory factory)
{
    private HttpClient Client(HttpClient? host = null) => host ?? factory.CreateClient();

    private async Task<(HttpClient Client, IDisposable Host)> ProbeAsync()
    {
        var host = factory.With<TimeProbeController>().Build();
        var client = host.CreateClient();
        var session = await client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        client.WithToken(session.AccessToken);
        return (client, host);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ── Out: what the API says ──────────────────────────────────────────────────────

    [Fact]
    public async Task Instants_leave_the_api_as_utc_with_a_z_whatever_their_kind()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var data = await (await client.GetAsync("/test/time/out")).DataAsync();

        Assert.Equal("2026-10-10T09:30:00Z", data.GetProperty("stamped").GetString());   // unspecified is taken as UTC
        Assert.Equal("2026-10-10T09:30:00.123Z", data.GetProperty("withFraction").GetString());
        var expectedLocal = new DateTime(2026, 10, 10, 14, 30, 0, DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(expectedLocal.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), data.GetProperty("fromLocal").GetString());   // local is converted
        Assert.Equal(JsonValueKind.Null, data.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task A_business_date_leaves_the_api_as_a_plain_date_with_no_time_and_no_zone()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var data = await (await client.GetAsync("/test/time/out")).DataAsync();

        Assert.Equal("2026-10-10", data.GetProperty("day").GetString());
    }

    [Fact]
    public async Task An_instant_kept_with_its_own_offset_still_carries_it()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var data = await (await client.GetAsync("/test/time/out")).DataAsync();

        Assert.Equal("2026-10-10T14:30:00+05:00", data.GetProperty("withOffset").GetString());
    }

    [Fact]
    public async Task Real_endpoints_now_say_which_zone_their_timestamps_are_in()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync($"utc.{Guid.NewGuid():N}@test.local", "Passw0rd!Test");

        var user = await (await admin.GetAsync($"/api/users/{userId}")).DataAsync();

        var created = user.GetProperty("createdDate").GetString()!;
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?Z$", created);
        Assert.InRange(DateTime.Parse(created, null, System.Globalization.DateTimeStyles.RoundtripKind), DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddSeconds(5));
    }

    // ── In: what the API accepts ────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-10T14:30:00+05:00", "2026-10-10T09:30:00Z")]
    [InlineData("2026-10-10T09:30:00Z", "2026-10-10T09:30:00Z")]
    [InlineData("2026-10-10T09:30:00z", "2026-10-10T09:30:00Z")]
    [InlineData("2026-10-10T01:30:00.5-08:00", "2026-10-10T09:30:00.5Z")]
    [InlineData("2026-10-10T14:30:00+0500", "2026-10-10T09:30:00Z")]
    [InlineData("2026-10-10T23:30:00-05:00", "2026-10-11T04:30:00Z")]   // crosses midnight
    public async Task An_instant_with_any_offset_is_converted_to_utc(string sent, string expected)
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var response = await client.PostAsync("/test/time/in", Json($$"""{ "at": "{{sent}}", "day": "2026-10-10" }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("at").GetString());
        Assert.Equal("Utc", body.RootElement.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("\"2026-10-10T14:30:00\"")]      // no offset: whose clock?
    [InlineData("\"2026-10-10\"")]               // a date is not an instant
    [InlineData("\"14:30\"")]
    [InlineData("\"10/10/2026 2:30 PM +05:00\"")] // not ISO 8601
    [InlineData("\"\"")]
    [InlineData("1791624600000")]                 // epoch milliseconds
    [InlineData("null")]
    public async Task An_instant_without_an_offset_or_in_another_shape_is_refused(string value)
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var response = await client.PostAsync("/test/time/in", Json($$"""{ "at": {{value}}, "day": "2026-10-10" }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_optional_instant_may_be_left_out_or_null()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var response = await client.PostAsync("/test/time/in", Json("""{ "at": "2026-10-10T09:30:00Z", "maybeAt": null, "day": "2026-10-10" }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("maybeAt").ValueKind);
    }

    [Fact]
    public async Task A_business_date_comes_back_exactly_as_sent_whatever_the_servers_zone()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        // 31 December and 1 January are where a stray zone conversion would show first.
        foreach (var day in new[] { "2026-12-31", "2027-01-01", "2028-02-29", "2026-03-29" })
        {
            var response = await client.PostAsync("/test/time/in", Json($$"""{ "at": "2026-10-10T09:30:00Z", "day": "{{day}}" }"""));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(day, body.RootElement.GetProperty("day").GetString());
        }
    }

    [Theory]
    [InlineData("2026-10-10T00:00:00Z")]   // a business date carries no time
    [InlineData("2026-10-10T00:00:00")]
    [InlineData("10/10/2026")]
    [InlineData("2026-13-40")]
    public async Task A_business_date_with_a_time_or_in_another_shape_is_refused(string day)
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;

        var response = await client.PostAsync("/test/time/in", Json($$"""{ "at": "2026-10-10T09:30:00Z", "day": "{{day}}" }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── In the database ─────────────────────────────────────────────────────────────

    private sealed class DtRow
    {
        public int Id { get; set; }
        public DateTime At { get; set; }
        public DateTime? MaybeAt { get; set; }
        public DateOnly Day { get; set; }
    }

    private class DtContext(string connectionString, bool utcConvention) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlServer(connectionString);

        protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
        {
            if (utcConvention) configuration.UseUtcDateTimes();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DtRow>().ToTable("DtProbe", "dbo").HasKey(x => x.Id);
    }

    /// <summary>The same model without the convention. A separate type, because EF caches one model per context type.</summary>
    private sealed class PlainDtContext(string connectionString) : DtContext(connectionString, utcConvention: false);

    private DtContext NewContext(bool utcConvention = true)
    {
        factory.CreateClient();
        factory.Execute("IF OBJECT_ID('dbo.DtProbe') IS NULL CREATE TABLE dbo.DtProbe (Id int IDENTITY PRIMARY KEY, At datetime2 NOT NULL, MaybeAt datetime2 NULL, Day date NOT NULL)");
        return utcConvention ? new DtContext(factory.ConnectionString, true) : new PlainDtContext(factory.ConnectionString);
    }

    [Fact]
    public async Task A_local_time_is_stored_as_the_utc_instant_it_stands_for()
    {
        await using var db = NewContext();
        var local = new DateTime(2026, 10, 10, 14, 30, 0, DateTimeKind.Local);
        var row = new DtRow { At = local, Day = new DateOnly(2026, 10, 10) };
        db.Add(row);

        await db.SaveChangesAsync();

        Assert.Equal(local.ToUniversalTime(), factory.Scalar<DateTime>("SELECT At FROM dbo.DtProbe WHERE Id = @i", ("@i", row.Id)));
    }

    [Fact]
    public async Task A_utc_time_is_stored_as_it_is_and_an_unmarked_one_is_taken_to_be_utc()
    {
        await using var db = NewContext();
        var utc = new DtRow { At = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc), Day = new DateOnly(2026, 10, 10) };
        var unmarked = new DtRow { At = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Unspecified), Day = new DateOnly(2026, 10, 10) };
        db.AddRange(utc, unmarked);

        await db.SaveChangesAsync();

        var expected = new DateTime(2026, 10, 10, 9, 30, 0);
        Assert.Equal(expected, factory.Scalar<DateTime>("SELECT At FROM dbo.DtProbe WHERE Id = @i", ("@i", utc.Id)));
        Assert.Equal(expected, factory.Scalar<DateTime>("SELECT At FROM dbo.DtProbe WHERE Id = @i", ("@i", unmarked.Id)));
    }

    [Fact]
    public async Task What_comes_back_is_marked_as_utc_so_it_cannot_be_mistaken_for_local()
    {
        await using (var write = NewContext())
        {
            write.Add(new DtRow { At = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc), MaybeAt = new DateTime(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc), Day = new DateOnly(2026, 10, 10) });
            write.Add(new DtRow { At = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc), MaybeAt = null, Day = new DateOnly(2026, 10, 10) });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var rows = await read.Set<DtRow>().AsNoTracking().OrderByDescending(r => r.Id).Take(2).ToListAsync();

        Assert.All(rows, r => Assert.Equal(DateTimeKind.Utc, r.At.Kind));
        Assert.Equal(DateTimeKind.Utc, rows.Single(r => r.MaybeAt is not null).MaybeAt!.Value.Kind);
        Assert.Contains(rows, r => r.MaybeAt is null);
    }

    [Fact]
    public async Task Without_the_convention_a_read_instant_has_no_kind_which_is_the_bug_it_prevents()
    {
        await using (var write = NewContext())
        {
            write.Add(new DtRow { At = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc), Day = new DateOnly(2026, 10, 10) });
            await write.SaveChangesAsync();
        }

        await using var read = NewContext(utcConvention: false);
        var row = await read.Set<DtRow>().AsNoTracking().OrderByDescending(r => r.Id).FirstAsync();

        Assert.Equal(DateTimeKind.Unspecified, row.At.Kind);
    }

    [Fact]
    public async Task A_business_date_is_stored_in_a_date_column_untouched()
    {
        await using var db = NewContext();
        var row = new DtRow { At = DateTime.UtcNow, Day = new DateOnly(2026, 12, 31) };
        db.Add(row);

        await db.SaveChangesAsync();

        Assert.Equal("2026-12-31", factory.Scalar<string>("SELECT CONVERT(varchar(10), Day, 23) FROM dbo.DtProbe WHERE Id = @i", ("@i", row.Id)));
        Assert.Equal("date", factory.Scalar<string>("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DtProbe' AND COLUMN_NAME = 'Day'"));
    }

    [Fact]
    public void Every_instant_column_of_every_module_uses_the_utc_convention()
    {
        // A guard for the future: a new module's DbContext that forgets UseUtcDateTimes() fails here.
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var contexts = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name!.StartsWith("VMS.Modules", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();
        Assert.True(contexts.Count >= 3, "Expected to find the Auth, Tenancy and Core contexts.");

        foreach (var type in contexts)
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(type);
            var unconverted = db.Model.GetEntityTypes()
                .SelectMany(e => e.GetProperties().Select(p => (Entity: e.ClrType.Name, Property: p)))
                .Where(x => x.Property.ClrType == typeof(DateTime) || x.Property.ClrType == typeof(DateTime?))
                .Where(x => x.Property.GetValueConverter() is null)
                .Select(x => $"{type.Name}.{x.Entity}.{x.Property.Name}")
                .ToList();

            Assert.True(unconverted.Count == 0, "These instants are not stored as UTC: " + string.Join(", ", unconverted));
        }
    }

    // ── The company's calendar ──────────────────────────────────────────────────────

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> At(string utc) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTime(DateTimeOffset.Parse(utc))))));

    private Guid TenantIn(string? timeZone) => factory.CreateTenant(timeZone);

    [Theory]
    [InlineData("2026-12-31T18:59:00Z", "2026-12-31")]   // 23:59 in Karachi
    [InlineData("2026-12-31T19:00:00Z", "2027-01-01")]   // midnight in Karachi
    public async Task With_no_zone_of_its_own_a_tenant_keeps_the_platform_default_calendar(string now, string expected)
    {
        using var host = At(now);
        host.CreateClient();
        using var scope = host.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IOperatingClock>();

        Assert.Equal(DateOnly.Parse(expected), await clock.TodayAsync(TenantIn(timeZone: null)));
        Assert.Equal("Asia/Karachi", clock.DefaultTimeZoneId);
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", "2026-12-31T12:00:00Z", "2027-01-01")]   // UTC+14: already the new year
    [InlineData("Pacific/Pago_Pago", "2027-01-01T05:00:00Z", "2026-12-31")]    // UTC-11: still the old one
    [InlineData("Asia/Karachi", "2026-06-30T19:00:00Z", "2026-07-01")]
    public async Task A_tenant_with_a_zone_is_on_its_own_calendar_day(string zone, string now, string expected)
    {
        using var host = At(now);
        host.CreateClient();
        using var scope = host.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IOperatingClock>();

        Assert.Equal(DateOnly.Parse(expected), await clock.TodayAsync(TenantIn(zone)));
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("")]
    public async Task An_unusable_zone_falls_back_to_the_default_rather_than_failing(string zone)
    {
        using var host = At("2026-12-31T19:00:00Z");
        host.CreateClient();
        using var scope = host.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IOperatingClock>();

        Assert.Equal(new DateOnly(2027, 1, 1), await clock.TodayAsync(TenantIn(zone)));
        Assert.Equal(new DateOnly(2027, 1, 1), await clock.TodayAsync(Guid.NewGuid()));   // and so does an unknown tenant
    }

    [Fact]
    public async Task A_number_without_a_date_takes_its_year_from_the_tenants_calendar()
    {
        var pagoPago = TenantIn("Pacific/Pago_Pago");      // 2027-01-01 05:00Z is still 31 December there
        var kiritimati = TenantIn("Pacific/Kiritimati");   // and 2026-12-31 12:00Z is already 1 January there

        async Task<string> Next(string now, Guid tenant)
        {
            using var host = At(now);
            host.CreateClient();
            using var scope = host.Services.CreateScope();
            var series = scope.ServiceProvider.GetRequiredService<INumberSeries>();
            await using var db = new DbContext(new DbContextOptionsBuilder<DbContext>().UseSqlServer(factory.ConnectionString).Options);
            return await db.InTransactionAsync(ct => series.NextAsync(db, "BP", null, tenant, ct));
        }

        Assert.Equal("BP-26-00001", await Next("2027-01-01T05:00:00Z", pagoPago));
        Assert.Equal("BP-27-00001", await Next("2026-12-31T12:00:00Z", kiritimati));
    }

    // ── The tenant's zone ───────────────────────────────────────────────────────────

    private async Task<Guid> CreateTenantAsync(HttpClient admin, string? timeZone)
    {
        var response = await admin.PostAsJsonAsync("/api/system/tenants", new
        {
            tenantCode = "TZ" + Guid.NewGuid().ToString("N")[..8], tenantName = "Zone Transport",
            adminFirstName = "Zed", adminEmail = $"zone.{Guid.NewGuid():N}@test.local", timeZone
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.DataAsync()).GetProperty("tenantId").GetGuid();
    }

    [Fact]
    public async Task A_tenant_time_zone_that_does_not_exist_is_refused_rather_than_silently_ignored()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/system/tenants", new
        {
            tenantCode = "TZ" + Guid.NewGuid().ToString("N")[..8], tenantName = "Typo Transport",
            adminFirstName = "Ty", adminEmail = $"typo.{Guid.NewGuid():N}@test.local", timeZone = "Asia/Karchi"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("'Asia/Karchi' is not a known time zone. Use a name such as Asia/Karachi.", await response.MessageAsync());
    }

    [Fact]
    public async Task A_tenant_may_leave_its_time_zone_blank_to_use_the_platform_default()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var id = await CreateTenantAsync(admin, timeZone: "  ");

        Assert.Null(factory.Scalar<string?>("SELECT TimeZone FROM tenancy.Tenants WHERE Id = @i", ("@i", id)));
    }

    [Fact]
    public async Task Changing_a_tenants_time_zone_applies_at_once_not_after_the_cache_expires()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var id = await CreateTenantAsync(admin, "Pacific/Pago_Pago");   // UTC-11
        using var scope = factory.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IOperatingClock>();
        var before = await clock.TodayAsync(id);   // this caches the tenant's snapshot

        var response = await admin.PutAsJsonAsync($"/api/system/tenants/{id}", new { tenantName = "Zone Transport", timeZone = "Pacific/Kiritimati" });   // UTC+14
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 25 hours apart, so Kiritimati's calendar is always at least a day ahead of Pago Pago's.
        Assert.True(await clock.TodayAsync(id) > before);
    }

    [Fact]
    public async Task Updating_a_tenant_with_a_bad_time_zone_changes_nothing()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var id = await CreateTenantAsync(admin, "Asia/Karachi");

        var response = await admin.PutAsJsonAsync($"/api/system/tenants/{id}", new { tenantName = "Renamed", timeZone = "Mars/Olympus" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Zone Transport", factory.Scalar<string>("SELECT TenantName FROM tenancy.Tenants WHERE Id = @i", ("@i", id)));
        Assert.Equal("Asia/Karachi", factory.Scalar<string>("SELECT TimeZone FROM tenancy.Tenants WHERE Id = @i", ("@i", id)));
    }

    // ── The client's zone ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Asia/Karachi", 5.0)]
    [InlineData("Pacific/Pago_Pago", -11.0)]
    [InlineData("Not/AZone", null)]
    [InlineData("", null)]
    public async Task The_zone_a_browser_reports_is_available_to_exports_and_unknown_ones_are_ignored(string header, double? expectedHours)
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;
        if (header.Length > 0) client.DefaultRequestHeaders.Add("X-Time-Zone", header);

        var body = JsonDocument.Parse(await client.GetStringAsync("/test/time/zone")).RootElement.GetProperty("offsetHours");

        if (expectedHours is null) Assert.Equal(JsonValueKind.Null, body.ValueKind);
        else Assert.Equal(expectedHours.Value, body.GetDouble());
    }

    [Fact]
    public async Task An_absurdly_long_zone_header_is_ignored()
    {
        var (client, host) = await ProbeAsync();
        using var _ = host; using var __ = client;
        client.DefaultRequestHeaders.Add("X-Time-Zone", new string('A', 500));

        var body = JsonDocument.Parse(await client.GetStringAsync("/test/time/zone")).RootElement.GetProperty("offsetHours");

        Assert.Equal(JsonValueKind.Null, body.ValueKind);
    }
}
