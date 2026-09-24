using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>Pure date math of a recurring charge (FSD §19A.1), with no database — S4-REC-01/05.</summary>
public sealed class ChargeScheduleTests
{
    [Theory]
    [InlineData("2026-01-15", "Monthly", 31, "2026-01-31")]   // 29-31 falls back to month end, like the finance schedule
    [InlineData("2026-02-15", "Monthly", 31, "2026-02-28")]
    [InlineData("2026-01-20", "Monthly", 5, "2026-02-05")]    // due day already passed this month: first occurrence is next month
    [InlineData("2026-01-05", "Monthly", 5, "2026-01-05")]    // due day is today: today is the first occurrence
    public void First_due_falls_on_the_configured_day_or_the_month_end(string from, string frequency, int dueDay, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ChargeSchedule.FirstDue(DateOnly.Parse(from), frequency, dueDay, null));

    [Fact]
    public void Yearly_anniversaries_step_a_year_at_a_time()
    {
        var first = ChargeSchedule.FirstDue(new DateOnly(2026, 3, 1), ChargeFrequencies.Yearly, 15, 6);
        Assert.Equal(new DateOnly(2026, 6, 15), first);
        Assert.Equal(new DateOnly(2027, 6, 15), ChargeSchedule.Next(first, ChargeFrequencies.Yearly, 15, null));
    }

    [Fact]
    public void Quarterly_and_half_yearly_step_by_three_and_six_months_and_keep_falling_back_to_month_end()
    {
        var q = ChargeSchedule.Next(new DateOnly(2026, 1, 31), ChargeFrequencies.Quarterly, 31, null);
        Assert.Equal(new DateOnly(2026, 4, 30), q);   // April has 30 days
        var h = ChargeSchedule.Next(new DateOnly(2026, 1, 31), ChargeFrequencies.HalfYearly, 31, null);
        Assert.Equal(new DateOnly(2026, 7, 31), h);
    }

    [Fact]
    public void Weekly_and_custom_days_step_by_days_not_a_calendar_field()
    {
        Assert.Equal(new DateOnly(2026, 1, 8), ChargeSchedule.Next(new DateOnly(2026, 1, 1), ChargeFrequencies.Weekly, null, null));
        Assert.Equal(new DateOnly(2026, 1, 15), ChargeSchedule.Next(new DateOnly(2026, 1, 1), ChargeFrequencies.CustomDays, null, 14));
    }

    [Theory]
    [InlineData("Monthly", "2026-03-15", "2026-03")]
    [InlineData("Quarterly", "2026-03-15", "2026-03")]
    [InlineData("Yearly", "2026-03-15", "2026")]
    [InlineData("Weekly", "2026-03-15", "2026-03-15")]
    [InlineData("CustomDays", "2026-03-15", "2026-03-15")]
    public void Period_keys_are_unique_per_occurrence_not_per_charge(string frequency, string due, string expectedKey) =>
        Assert.Equal(expectedKey, ChargeSchedule.PeriodKeyOf(DateOnly.Parse(due), frequency));
}

/// <summary>S4-REC-01 to S4-REC-11: recurring charges configured on a vehicle, generated nightly, confirmed, waived, cancelled and end-dated.</summary>
[Collection(ApiCollection.Name)]
public sealed class RecurringChargeTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");
    private static int TodayDay => DateOnly.FromDateTime(DateTime.UtcNow).Day;

    private static async Task<int> ChargeTypeAsync(VehicleWorld w, string description) =>
        (await (await w.Admin.GetAsync("/api/lookups/RECURRING_CHARGE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == description).GetProperty("id").GetInt32();

    private static async Task<int> ExpenseTypeAsync(VehicleWorld w, string description) =>
        (await (await w.Admin.GetAsync("/api/lookups/EXPENSE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == description).GetProperty("id").GetInt32();

    /// <summary>A vehicle in the fleet, self-owned, acquired a week ago.</summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle)> FleetVehicleAsync(VehicleWorld? world = null)
    {
        var w = world ?? await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync(acquired: Day(-30));
        return (w, vehicle);
    }

    private static JsonObject TrackerFeeBody(int chargeTypeId, int expenseTypeId, int payeeId, string startDate, decimal amount = 5_000, int dueDay = -1, string postingMode = "GenerateAsDue") => new()
    {
        ["chargeTypeId"] = chargeTypeId, ["payeeId"] = payeeId, ["expenseTypeId"] = expenseTypeId, ["amount"] = amount, ["amountBasis"] = "Fixed",
        ["frequency"] = "Monthly", ["dueDay"] = dueDay < 0 ? TodayDay : dueDay, ["startDate"] = startDate, ["postingMode"] = postingMode, ["generateLeadDays"] = 7
    };

    private static Task<HttpResponseMessage> CreateChargeAsync(VehicleWorld w, JsonObject vehicle, JsonObject body, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges", body);

    // ── Configuring ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tracker_fee_is_configured_with_a_next_due_date_and_lists_as_current()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");

        var response = await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1)));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var charge = PartnerWorld.AsObject(await response.DataAsync());
        Assert.Equal("GenerateAsDue", charge["postingMode"]!.GetValue<string>());
        Assert.True(charge["isActive"]!.GetValue<bool>());
        Assert.NotNull(charge["nextDueDate"]);

        var list = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges")).DataAsync()).EnumerateArray().ToList();
        Assert.Single(list);
    }

    [Fact]
    public async Task The_payee_must_hold_the_role_the_charge_type_implies()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var wrongRolePartner = await w.PartnerAsync("Vendor");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");

        var response = await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, wrongRolePartner, Day(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Tracker Company", await response.MessageAsync());
    }

    [Fact]
    public async Task An_end_date_and_a_number_of_occurrences_cannot_both_be_set()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var body = TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1));
        body["endDate"] = Day(400);
        body["occurrenceCount"] = 12;

        var response = await CreateChargeAsync(w, vehicle, body);

        await PartnerWorld.AssertRefusedAsync(response, "endDate", Msg.VhEndDateOrOccurrences);
    }

    [Fact]
    public async Task Auto_post_is_refused_for_anything_but_a_fixed_amount()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var body = TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), postingMode: "AutoPost");
        body["amountBasis"] = "Variable";
        body["amount"] = null;

        var response = await CreateChargeAsync(w, vehicle, body);

        await PartnerWorld.AssertRefusedAsync(response, "postingMode", Msg.VhAutoPostNeedsFixedAmount);
    }

    [Fact]
    public async Task Auto_post_needs_the_auto_post_permission_even_with_manage_permission()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var noAutoPost = w.As("Finance Clerk", 501, PermissionCodes.VEH_VIEW, PermissionCodes.FIN_RECURRING_MANAGE);

        var response = await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), postingMode: "AutoPost"), noAutoPost);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_manual_bank_installment_charge_is_refused_once_the_vehicle_has_an_active_agreement()
    {
        var (w, vehicle, bank) = await LeasedAsync(factory);
        var chargeType = await ChargeTypeAsync(w, "Bank Installment");
        var expenseType = await ExpenseTypeAsync(w, "Other");

        var response = await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, bank, Day(-1)));

        await PartnerWorld.AssertRefusedAsync(response, "chargeTypeId", Msg.VhBankInstallmentAlreadyFinanced);
    }

    [Fact]
    public async Task Amending_ends_the_current_row_and_starts_a_new_one_that_keeps_the_same_series()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var created = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-10)))).DataAsync());

        var amend = TrackerFeeBody(chargeType, expenseType, trackerCo, Day(1), amount: 6_500);
        amend["rowVersion"] = created["rowVersion"]!.GetValue<string>();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/{created["id"]!.GetValue<int>()}", amend);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var replacement = PartnerWorld.AsObject(await response.DataAsync());

        Assert.Equal(created["seriesId"]!.GetValue<Guid>(), replacement["seriesId"]!.GetValue<Guid>());
        Assert.NotEqual(created["id"]!.GetValue<int>(), replacement["id"]!.GetValue<int>());
        Assert.Equal(6_500m, replacement["amount"]!.GetValue<decimal>());

        var list = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.Equal(2, list.Count);
        var old = list.Single(c => c["id"]!.GetValue<int>() == created["id"]!.GetValue<int>());
        Assert.False(old["isActive"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Ending_a_charge_by_hand_needs_a_reason_and_stops_its_next_due_date()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var created = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1)))).DataAsync());

        var missingReason = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/{created["id"]!.GetValue<int>()}/end", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var ended = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/{created["id"]!.GetValue<int>()}/end", new { reason = "Contract cancelled" });
        Assert.True(ended.IsSuccessStatusCode, await ended.Content.ReadAsStringAsync());
        var row = PartnerWorld.AsObject(await ended.DataAsync());
        Assert.False(row["isActive"]!.GetValue<bool>());
        Assert.Null(row["nextDueDate"]);
    }

    // ── Generation (S4-REC-05, S4-REC-10) ──────────────────────────────────────────────

    private static Task<HttpResponseMessage> RunJobAsync(VehicleWorld w) => w.Admin.PostAsJsonAsync("/api/admin/jobs/recurring-charges/run", new { });

    private static async Task<List<JsonObject>> PayablesAsync(VehicleWorld w, JsonObject vehicle) =>
        (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/payables")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();

    [Fact]
    public async Task The_job_generates_nothing_before_the_lead_day_window_opens()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var body = TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), dueDay: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20).Day);
        body["startDate"] = Day(-1);
        await CreateChargeAsync(w, vehicle, body);

        var result = PartnerWorld.AsObject(await (await RunJobAsync(w)).DataAsync());
        Assert.Equal(0, result["entriesGenerated"]!.GetValue<int>());
        Assert.Empty(await PayablesAsync(w, vehicle));
    }

    [Fact]
    public async Task The_job_generates_a_due_entry_once_the_window_opens_and_running_it_again_makes_no_duplicate()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), amount: 5_000));   // due today: inside the default 7-day lead window

        var first = PartnerWorld.AsObject(await (await RunJobAsync(w)).DataAsync());
        Assert.Equal(1, first["entriesGenerated"]!.GetValue<int>());

        var second = PartnerWorld.AsObject(await (await RunJobAsync(w)).DataAsync());
        Assert.Equal(0, second["entriesGenerated"]!.GetValue<int>());   // BR-VH-030: idempotent — no duplicate for the same period

        var payables = await PayablesAsync(w, vehicle);
        var entry = Assert.Single(payables);
        Assert.Equal("RecurringCharge", entry["kind"]!.GetValue<string>());
        Assert.Equal(5_000m, entry["expectedAmount"]!.GetValue<decimal>());

        // BR-VH-030 again, the harder way: the cursor is put back on the period already generated (as a confused restart might find it), and the job must still recognise the period as done rather than generate a second row for it.
        var chargeId = factory.Scalar<int>("SELECT VehicleRecurringChargeId FROM veh.VehicleRecurringChargeEntries WHERE VehicleId = @v", ("@v", Id(vehicle)));
        var alreadyGeneratedDue = DateOnly.FromDateTime(factory.Scalar<DateTime>("SELECT DueDate FROM veh.VehicleRecurringChargeEntries WHERE VehicleId = @v", ("@v", Id(vehicle))));
        factory.Execute("UPDATE veh.VehicleRecurringCharges SET NextDueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", alreadyGeneratedDue), ("@id", chargeId));
        await RunJobAsync(w);
        var count = factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleRecurringChargeEntries WHERE VehicleId = @v", ("@v", Id(vehicle)));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Auto_post_immediately_posts_a_system_generated_ledger_entry()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), amount: 3_000, postingMode: "AutoPost"));

        var result = PartnerWorld.AsObject(await (await RunJobAsync(w)).DataAsync());
        Assert.Equal(1, result["entriesGenerated"]!.GetValue<int>());
        Assert.Equal(1, result["entriesAutoPosted"]!.GetValue<int>());
        Assert.Empty(await PayablesAsync(w, vehicle));   // already paid: nothing left due

        var ledger = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/transactions")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        var posted = Assert.Single(ledger, t => t["type"]!.GetValue<string>() == "RecurringCharge");
        Assert.True(posted["isSystemGenerated"]!.GetValue<bool>());
        Assert.Equal(3_000m, posted["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task An_entry_due_in_the_past_becomes_overdue()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var created = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-20)))).DataAsync());
        // Back-date the due day so the period is already in the past, without waiting real calendar time.
        factory.Execute("UPDATE veh.VehicleRecurringCharges SET NextDueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", DateOnly.Parse(Day(-5))), ("@id", created["id"]!.GetValue<int>()));

        await RunJobAsync(w);

        var entry = Assert.Single(await PayablesAsync(w, vehicle));
        Assert.Equal("Overdue", entry["status"]!.GetValue<string>());
    }

    // ── Confirming, waiving, cancelling (S4-REC-08, S4-REC-09) ──────────────────────────

    private async Task<JsonObject> DueEntryAsync(VehicleWorld w, JsonObject vehicle, decimal amount = 5_000)
    {
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), amount: amount));
        await RunJobAsync(w);
        return (await PayablesAsync(w, vehicle)).Single();
    }

    [Fact]
    public async Task Confirming_an_entry_posts_the_actual_amount_and_retains_the_expected_one()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var due = await DueEntryAsync(w, vehicle, 5_000);

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/confirm",
            new { amount = 5_250, paidOn = Day(0), paymentMode = "Cheque", reference = "CH-1" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        Assert.Empty(await PayablesAsync(w, vehicle));
        var ledger = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/transactions")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.Equal(5_250m, Assert.Single(ledger, t => t["type"]!.GetValue<string>() == "RecurringCharge")["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Confirming_needs_both_the_due_confirm_and_the_cost_view_permission()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var due = await DueEntryAsync(w, vehicle);
        var noCostView = w.As("No Cost View", 502, PermissionCodes.VEH_VIEW, PermissionCodes.FIN_DUE_CONFIRM);

        var response = await noCostView.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/confirm", new { amount = 5_000, paidOn = Day(0) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Waiving_needs_a_reason_and_sets_the_entry_aside_without_posting()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var due = await DueEntryAsync(w, vehicle);

        var missing = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/waive", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var waived = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/waive", new { reason = "Vendor waived the fee" });
        Assert.True(waived.IsSuccessStatusCode, await waived.Content.ReadAsStringAsync());
        Assert.Empty(await PayablesAsync(w, vehicle));
        var ledger = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/transactions")).DataAsync()).EnumerateArray().ToList();
        Assert.DoesNotContain(ledger, t => t.GetProperty("type").GetString() == "RecurringCharge");
    }

    [Fact]
    public async Task A_paid_entry_cannot_be_confirmed_waived_or_cancelled_again()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var due = await DueEntryAsync(w, vehicle);
        await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/confirm", new { amount = 5_000, paidOn = Day(0) });

        var again = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/confirm", new { amount = 5_000, paidOn = Day(0) });
        var waive = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges/entries/{due["id"]!.GetValue<int>()}/waive", new { reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, waive.StatusCode);
    }

    // ── Cascades: disposal and category change (S4-REC-11) ──────────────────────────────

    [Fact]
    public async Task Disposing_a_vehicle_end_dates_its_charges_and_cancels_only_the_not_yet_due_entries()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var soonDue = await DueEntryAsync(w, vehicle, 4_000);   // already generated as Due before disposal
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var future = TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1), dueDay: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60).Day);
        var futureCharge = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, future)).DataAsync());

        var disposed = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/dispose", new { kind = "Retire", date = Day(0), reason = "End of life" });
        Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());

        var charges = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.All(charges, c => Assert.False(c["isActive"]!.GetValue<bool>()));
        Assert.All(charges, c => Assert.Equal("Vehicle disposed", c["endReason"]!.GetValue<string>()));
        _ = futureCharge;

        // The already-due entry is untouched (still payable); nothing new was generated for the ended, never-due charge, so there is nothing to cancel either.
        var entries = factory.Query("SELECT Status FROM veh.VehicleRecurringChargeEntries WHERE VehicleId = @v", ("@v", Id(vehicle)));
        Assert.Single(entries);
        Assert.Equal("Due", entries[0]["Status"]);
        _ = soonDue;
    }

    [Fact]
    public async Task Disposing_a_vehicle_cancels_an_already_generated_entry_that_is_not_yet_due()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var created = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, trackerCo, Day(-1)))).DataAsync());
        await RunJobAsync(w);   // one Due entry, due today
        // Move it into the future so disposal today must cancel it (not yet due).
        factory.Execute("UPDATE veh.VehicleRecurringChargeEntries SET DueDate = @d WHERE VehicleRecurringChargeId = @c", ("@d", DateOnly.Parse(Day(10))), ("@c", created["id"]!.GetValue<int>()));

        await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/dispose", new { kind = "Retire", date = Day(0), reason = "End of life" });

        var status = factory.Scalar<string>("SELECT Status FROM veh.VehicleRecurringChargeEntries WHERE VehicleRecurringChargeId = @c", ("@c", created["id"]!.GetValue<int>()));
        Assert.Equal("Cancelled", status);
    }

    [Fact]
    public async Task Changing_category_away_from_rented_end_dates_the_rent_payable_charge()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var lessor = await w.PartnerAsync("Vendor");
        var created = await w.CreateAsync(w.Truck());
        factory.Execute("UPDATE veh.Vehicles SET Status = 'Active', CurrentCategory = 'Rented', AcquisitionDate = @a WHERE VehicleId = @v", ("@v", Id(created)), ("@a", DateOnly.Parse(Day(-60))));
        factory.Execute(
            "INSERT INTO veh.VehicleRelations (TenantId, VehicleId, Category, CounterpartyId, EffectiveFrom, RentAmount, RentFrequency, RentDueDay) VALUES (@t, @v, 'Rented', @c, @f, 20000, 'Monthly', 5)",
            ("@t", w.Tenant), ("@v", Id(created)), ("@c", lessor), ("@f", DateOnly.Parse(Day(-60))));
        var vehicle = await w.GetAsync(Id(created));

        var rentPayee = await w.PartnerAsync("Vendor");
        var chargeType = await ChargeTypeAsync(w, "Vehicle Rent Payable");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var charge = PartnerWorld.AsObject(await (await CreateChargeAsync(w, vehicle, TrackerFeeBody(chargeType, expenseType, rentPayee, Day(-30)))).DataAsync());

        var changed = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/category", new
        {
            category = "SelfOwned", effectiveDate = Day(0), reason = "Bought outright", details = new { }
        });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        var row = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges")).DataAsync()).EnumerateArray()
            .Select(PartnerWorld.AsObject).Single(c => c["id"]!.GetValue<int>() == charge["id"]!.GetValue<int>());
        Assert.False(row["isActive"]!.GetValue<bool>());
        Assert.Equal("Ownership category changed", row["endReason"]!.GetValue<string>());
    }

    // ── Payables: Bank Installment surfacing without duplicating the schedule (BR-VH-029) ─

    /// <summary>An activated bank-leased vehicle with exactly one installment, already overdue.</summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle, int Bank)> LeasedAsync(ApiFactory f)
    {
        var w = await VehicleWorld.CreateAsync(f);
        var vehicle = await w.CreateAsync(w.Truck());
        var bank = await w.PartnerAsync("Bank");
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition")).DataAsync());
        await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", new
        {
            acquisitionDate = Day(-60), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer", rowVersion = block["rowVersion"]!.GetValue<string>()
        });
        var type = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();
        var saved = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", new
        {
            financeTypeId = type, bankId = bank, agreementNo = "AGR-" + Guid.NewGuid().ToString("N")[..8], agreementDate = Day(-59), financeAmount = 3_000_000, downPayment = 2_000_000,
            installmentAmount = 3_000_000, frequency = "Monthly", tenure = 1, firstDueDate = Day(-10)
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await w.UploadRegistrationBookAsync(Id(vehicle));   // BR-VH-015: a Bank Leased vehicle needs one on file to activate
        var fresh = await w.GetAsync(Id(vehicle));
        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activate", new { category = "BankLeased", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());
        return (w, vehicle, bank);
    }

    [Fact]
    public async Task A_bank_installment_shows_among_the_vehicles_payables_with_no_recurring_charge_row_behind_it()
    {
        var (w, vehicle, bank) = await LeasedAsync(factory);

        var payables = await PayablesAsync(w, vehicle);

        var installment = Assert.Single(payables, p => p["kind"]!.GetValue<string>() == "Installment");
        Assert.Equal("Overdue", installment["status"]!.GetValue<string>());   // first due date was 30 days ago
        Assert.Equal(bank, installment["payee"]!["id"]!.GetValue<int>());
        Assert.Empty(factory.Query("SELECT 1 FROM veh.VehicleRecurringCharges WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task Bulk_confirm_pays_an_installment_and_a_recurring_charge_in_one_call_and_reports_a_failure_without_stopping_the_rest()
    {
        var (w, vehicle, _) = await LeasedAsync(factory);
        var installmentDue = (await PayablesAsync(w, vehicle)).Single(p => p["kind"]!.GetValue<string>() == "Installment");
        var schedule = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/installments")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).First();
        var (w2, vehicle2) = await FleetVehicleAsync(w);
        var chargeDue = await DueEntryAsync(w2, vehicle2, 2_500);

        var response = await w.Admin.PostAsJsonAsync("/api/payables/bulk-confirm", new
        {
            paidOn = Day(0),
            paymentMode = "BankTransfer",
            items = new object[]
            {
                new { kind = "Installment", vehicleId = Id(vehicle), id = installmentDue["id"]!.GetValue<int>(), rowVersion = schedule["rowVersion"]!.GetValue<string>() },
                new { kind = "RecurringCharge", vehicleId = Id(vehicle2), id = chargeDue["id"]!.GetValue<int>() },
                new { kind = "RecurringCharge", vehicleId = Id(vehicle2), id = 999_999 },   // does not exist: must not stop the batch
            }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = PartnerWorld.AsObject(await response.DataAsync());

        Assert.Equal(2, result["succeeded"]!.GetValue<int>());
        Assert.Single(result["failed"]!.AsArray());
        Assert.Empty(await PayablesAsync(w, vehicle));
        Assert.Empty(await PayablesAsync(w2, vehicle2));
    }

    [Fact]
    public async Task The_summary_counts_what_is_due_soon_and_what_is_overdue()
    {
        var (w, vehicle, _) = await LeasedAsync(factory);

        var response = await w.Admin.GetAsync("/api/payables/summary");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var summary = PartnerWorld.AsObject(await response.DataAsync());
        Assert.True(summary["overdueCount"]!.GetValue<int>() >= 1);
        _ = vehicle;
    }

    [Fact]
    public async Task Viewing_the_workbench_and_confirming_from_it_need_the_due_confirm_permission()
    {
        var (w, _, _) = await LeasedAsync(factory);
        var noAccess = w.As("Fleet Manager", 503, PermissionCodes.VEH_VIEW);

        var list = await noAccess.GetAsync("/api/payables");
        var summary = await noAccess.GetAsync("/api/payables/summary");
        var bulk = await noAccess.PostAsJsonAsync("/api/payables/bulk-confirm", new { items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bulk.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_reach_recurring_charges_or_payables()
    {
        var (w, vehicle) = await FleetVehicleAsync();
        var anonymous = w.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{Id(vehicle)}/recurring-charges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/payables")).StatusCode);
    }
}
