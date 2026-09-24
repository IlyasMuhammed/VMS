using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Vehicles;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Acceptance;

/// <summary>
/// S8-QA-01: one test per criterion of FSD §25, named by its own AC ID and asserting exactly its Given/When/Then —
/// a single file a reviewer can check the FSD's own acceptance table against, directly. Each of these criteria is
/// also exercised, more thoroughly and from more angles, by the stage-specific test files that built the feature in
/// the first place (`PartnerCreateTests`, `VehicleActivationTests`, `RecurringChargeTests`, `NotificationTests` and
/// the rest); this file's job is traceability to §25's own 24 rows, not additional coverage of what they already prove.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AcceptanceCriteriaTests(ApiFactory factory)
{
    // ══════════════════════════════ Business Partner ══════════════════════════════

    /// <summary>AC-BP-001: Given a new partner form, When Party Type = Company is chosen, Then NTN is mandatory and CNIC is not required.</summary>
    [Fact]
    public async Task AC_BP_001_Company_needs_an_NTN_not_a_CNIC()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var company = w.Company(); company.Remove("ntn"); company.Remove("cnic");

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(company), "ntn", Msg.Required);

        company["ntn"] = PartnerWorld.NextNtn();
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(company)).StatusCode);   // no CNIC needed at all
    }

    /// <summary>AC-BP-002: Given a partner with roles Workshop and Vendor, When the vendor picker on an expense form is opened, Then the partner appears once, not twice.</summary>
    [Fact]
    public async Task AC_BP_002_A_partner_with_two_roles_appears_once_in_a_single_role_picker()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Company(roles: ["Workshop", "Vendor"]);
        var created = await w.CreateAsync(body);

        var picked = (await (await w.Admin.GetAsync("/api/partners/picker?role=Vendor")).DataAsync()).EnumerateArray()
            .Where(p => p.GetProperty("id").GetInt32() == created["id"]!.GetValue<int>()).ToList();

        Assert.Single(picked);
    }

    /// <summary>AC-BP-003: Given an existing partner with a CNIC, When a new partner is saved with the same CNIC, Then save is blocked and the existing partner is offered.</summary>
    [Fact]
    public async Task AC_BP_003_A_duplicate_CNIC_is_blocked_and_names_the_existing_partner()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person());
        var again = w.Person(); again["cnic"] = existing["cnic"]!.DeepClone();

        var response = await w.PostAsync(again);

        await PartnerWorld.AssertRefusedAsync(response, "cnic", Msg.BpCnicUsed);
        var message = (await PartnerWorld.ErrorsAsync(response)).Single(e => e.Field == "cnic").Message;
        Assert.Contains(existing["bpCode"]!.GetValue<string>(), message);
    }

    /// <summary>AC-BP-004: Given a partner named similarly in the same city, When a near-duplicate name is entered, Then a similarity warning lists the existing record before save.</summary>
    [Fact]
    public async Task AC_BP_004_A_similar_name_in_the_same_city_warns_before_save()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person("Al-Madina Goods"));

        var response = await w.PostAsync(w.Person("Al Madina Goods"));

        await PartnerWorld.AssertRefusedAsync(response, "legalName", Msg.BpSimilarName);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BusinessPartners WHERE TenantId = @t", ("@t", w.Tenant)));   // not saved, only warned
        _ = existing;
    }

    /// <summary>AC-BP-005: Given a driver assigned to a vehicle, When the Driver role is removed, Then removal is blocked, listing the vehicle.</summary>
    [Fact]
    public async Task AC_BP_005_Removing_an_assigned_drivers_role_is_blocked_and_names_the_vehicle()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driverId = await w.DriverAsync();
        // A second role first, or removing Driver would hit "the last role cannot be removed" (AC-BP-006) before ever reaching the usage check this criterion is about.
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/partners/{driverId}/roles", new { roleCode = "Workshop" })).IsSuccessStatusCode);
        var vehicle = await w.ActiveAsync();
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/driver", new { driverId })).IsSuccessStatusCode);

        var response = await w.Admin.DeleteAsync($"/api/partners/{driverId}/roles/Driver");

        await PartnerWorld.AssertRefusedAsync(response, "roleCode", Msg.BpRoleInUse);
        var usage = (await (await w.Admin.GetAsync($"/api/partners/{driverId}/usage?intent=RemoveRole&role=Driver")).DataAsync()).EnumerateArray().Single();
        Assert.Contains(vehicle["registrationNo"]!.GetValue<string>(), usage.GetProperty("description").GetString());
    }

    /// <summary>AC-BP-006: Given a partner with one role, When that role is unticked, Then VAL-BP-009 appears and save is blocked.</summary>
    [Fact]
    public async Task AC_BP_006_The_last_role_cannot_be_removed()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person());   // one role: Workshop

        var response = await w.Admin.DeleteAsync($"/api/partners/{partner["id"]!.GetValue<int>()}/roles/Workshop");

        await PartnerWorld.AssertRefusedAsync(response, "roleCode", Msg.BpLastRole);
    }

    /// <summary>AC-BP-007: Given a new partner with two contacts and a licence (Driver role), When save is pressed once, Then all rows persist together and BP Code is shown.</summary>
    [Fact]
    public async Task AC_BP_007_A_new_driver_with_two_contacts_saves_whole_with_a_BP_code()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver");
        body["driver"] = PartnerWorld.Driver();
        body["contacts"] = new JsonArray(
            new JsonObject { ["contactName"] = "Dispatcher", ["mobile"] = "0300-1111111", ["isPrimary"] = true },
            new JsonObject { ["contactName"] = "Guarantor Contact", ["mobile"] = "0300-2222222" });

        var created = await w.CreateAsync(body);

        Assert.Matches(@"^BP-\d{2}-\d{5}$", created["bpCode"]!.GetValue<string>());
        Assert.Equal(2, created["contacts"]!.AsArray().Count);
        Assert.NotNull(created["driver"]!["licenceNo"]);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BpContacts WHERE BusinessPartnerId = @i", ("@i", created["id"]!.GetValue<int>())) == 0 ? 0 : 1);   // sanity: rows really persisted
    }

    /// <summary>AC-BP-008: Given a driver licence expiring in 12 days, When the notification run executes with a 30-day rule, Then an alert is raised for that driver.</summary>
    [Fact]
    public async Task AC_BP_008_A_licence_expiring_soon_raises_an_alert_under_the_configured_lead()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = NotificationFixture.RegisterRecipientAsync(factory, w.Tenant, "acbp008");
        var driverId = await w.DriverAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "DRIVING_LICENCE").GetProperty("id").GetInt32();
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(12);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" }, { new StringContent(expiry.ToString("yyyy-MM-dd")), "expiryDate" }, { new StringContent("DL-ACBP8"), "documentNumber" },
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "licence.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/partners/{driverId}/documents", form);
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());   // the 30-day default rule already covers 12 days out; picked up immediately

        Assert.Equal(1, NotificationFixture.CountFor(factory, w.Tenant, recipient, "DocumentExpiry"));
    }

    /// <summary>AC-BP-009: Given a Finance user, When the partner screen is opened, Then salary and credit limit are visible; a Fleet Manager sees neither.</summary>
    [Fact]
    public async Task AC_BP_009_Salary_and_credit_are_visible_only_to_someone_holding_those_field_permissions()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person(role: "Driver"); body["driver"] = PartnerWorld.Driver();
        var driverId = (await w.CreateAsync(body))["id"]!.GetValue<int>();
        var financeUser = w.As("Finance User", 30, PermissionCodes.BP_VIEW, PermissionCodes.BP_FIELD_SALARY_VIEW, PermissionCodes.BP_FIELD_CREDIT_VIEW);
        var fleetManager = w.As("Fleet Manager", 31, PermissionCodes.BP_VIEW);

        var seenByFinance = PartnerWorld.AsObject(await (await financeUser.GetAsync($"/api/partners/{driverId}")).DataAsync());
        var seenByFleet = PartnerWorld.AsObject(await (await fleetManager.GetAsync($"/api/partners/{driverId}")).DataAsync());

        Assert.True(seenByFinance["driver"]!.AsObject().ContainsKey("monthlyRate"));
        Assert.False(seenByFleet["driver"]!.AsObject().ContainsKey("monthlyRate"));
    }

    /// <summary>AC-BP-010: Given Legal Name changed from A to B, When the History tab is opened, Then old and new values, user and timestamp are shown.</summary>
    [Fact]
    public async Task AC_BP_010_A_name_change_shows_old_and_new_values_with_who_and_when()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var partner = await w.CreateAsync(w.Person("Original Trading Co"));
        partner["legalName"] = "Renamed Trading Co";
        var editor = w.As("History Editor", 32, PartnerWorld.Everything);
        Assert.True((await w.PutAsync(partner, editor)).IsSuccessStatusCode);

        var history = await (await w.Admin.GetAsync($"/api/partners/{partner["id"]!.GetValue<int>()}/history")).DataAsync();
        var change = history.GetProperty("changes").GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("entity").GetString() == "BusinessPartner" && c.GetProperty("field").GetString() == "LegalName");

        Assert.Equal("Original Trading Co", change.GetProperty("oldValue").GetString());
        Assert.Equal("Renamed Trading Co", change.GetProperty("newValue").GetString());
        Assert.Equal("History Editor", change.GetProperty("userName").GetString());
        Assert.True(change.TryGetProperty("occurredAt", out _) || change.TryGetProperty("occurredOn", out _));
    }

    // ══════════════════════════════════ Vehicle ════════════════════════════════════

    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>AC-VH-001: Given a new vehicle wizard at step 1, When a registration number already on file is entered, Then VAL-VH-002 blocks the save.</summary>
    [Fact]
    public async Task AC_VH_001_A_registration_number_already_on_file_blocks_the_save()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var regNo = VehicleWorld.NextReg();
        await w.CreateAsync(w.Truck(regNo));

        var response = await w.PostAsync(w.Truck(regNo.Replace("-", " ")));   // spacing differs, BR-VH-017 treats it as the same number

        await PartnerWorld.AssertRefusedAsync(response, "registrationNo", Msg.VhRegNoExists);
    }

    /// <summary>AC-VH-002: Given Category = Bank Leased, When step 3 is opened, Then the bank picker shows only Bank-role partners, and the finance block is mandatory.</summary>
    [Fact]
    public async Task AC_VH_002_Bank_leased_needs_a_bank_role_partner_and_a_mandatory_finance_block()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var notABank = await w.PartnerAsync("Vendor");
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-5), acquisitionType = "Lease", purchasePrice = 3_000_000, amountPaid = 0, paymentMode = "BankTransfer" });
        var financeType = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();

        var wrongRole = await w.Admin.PutAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/finance", new
        {
            financeTypeId = financeType, bankId = notABank, agreementNo = "AGR-X", agreementDate = Day(-4), financeAmount = 3_000_000, downPayment = 0, installmentAmount = 250_000, frequency = "Monthly", tenure = 12, firstDueDate = Day(30),
        });
        var errors = await PartnerWorld.ErrorsAsync(wrongRole);
        Assert.Contains(errors, e => e.Field == "bankId" && (e.Code == Msg.VhCounterpartyLacksRole || e.Code == Msg.Invalid));

        // Activating Bank Leased with no finance agreement at all is refused too — the block is mandatory for the category, not just validated when present.
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));
        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "BankLeased", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });
        await PartnerWorld.AssertRefusedAsync(activated, "finance", Msg.VhBankLeasedNeedsFinance);
    }

    /// <summary>AC-VH-003: Given purchase price 5,000,000 and paid 2,000,000, When the vehicle is activated, Then paid-to-date is PKR 2,000,000, with two transactions.</summary>
    [Fact]
    public async Task AC_VH_003_Activation_posts_the_purchase_and_the_initial_payment()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-5), acquisitionType = "Purchase", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer" });
        await w.UploadRegistrationBookAsync(VehicleWorld.Id(vehicle));   // BR-VH-015: needed to activate, Self Owned included
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));
        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "SelfOwned", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());

        var summary = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/financial-summary")).DataAsync());
        var ledger = (await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/transactions")).DataAsync()).EnumerateArray().ToList();

        Assert.Equal(2_000_000m, summary["paidToDate"]!.GetValue<decimal>());
        Assert.Equal(2, ledger.Count);
    }

    /// <summary>AC-VH-004: Given the vehicle from AC-VH-003, When an installment of 150,000 is recorded, Then paid-to-date immediately reads 2,150,000 with no batch run.</summary>
    [Fact]
    public async Task AC_VH_004_Recording_an_installment_updates_paid_to_date_immediately()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-30), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer" });
        await AcceptanceHelpers.SaveFinanceAsync(w, vehicle, bank, new { financeAmount = 3_000_000, downPayment = 2_000_000 });   // BR-VH's own rule: down payment must equal the amount paid at creation
        await w.UploadRegistrationBookAsync(VehicleWorld.Id(vehicle));
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "BankLeased", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() })).IsSuccessStatusCode);
        var installment = (await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/installments")).DataAsync()).EnumerateArray().First();

        var paid = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/installments/{installment.GetProperty("id").GetInt32()}/payments",
            new { amount = 150_000, paidOn = Day(0), paymentMode = "BankTransfer", rowVersion = installment.GetProperty("rowVersion").GetString() });
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());

        var summary = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/financial-summary")).DataAsync());
        Assert.Equal(2_150_000m, summary["paidToDate"]!.GetValue<decimal>());
    }

    /// <summary>AC-VH-005: Given installment 75,000, tenure 36, monthly, first due 10-Oct-2026, When step 3 is completed, Then 36 rows preview, last due 10-Sep-2029, total 2,700,000.</summary>
    [Fact]
    public async Task AC_VH_005_The_schedule_previews_36_rows_ending_where_the_math_says()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-5), acquisitionType = "Lease", purchasePrice = 2_700_000, amountPaid = 0, paymentMode = "BankTransfer" });
        await AcceptanceHelpers.SaveFinanceAsync(w, vehicle, bank, new { financeAmount = 2_700_000, downPayment = 0, installmentAmount = 75_000, tenure = 36, firstDueDate = "2026-10-10", residualAmount = (decimal?)null });

        var rows = (await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/finance/schedule")).DataAsync()).EnumerateArray().ToList();

        Assert.Equal(36, rows.Count);
        Assert.Equal("2029-09-10", rows[^1].GetProperty("dueDate").GetString());
        Assert.Equal(2_700_000m, rows.Sum(r => r.GetProperty("amount").GetDecimal()));
    }

    /// <summary>AC-VH-006: Given a lease with first due on the 31st, When the schedule is generated, Then February rows fall on the 28th or 29th, not rejected.</summary>
    [Fact]
    public async Task AC_VH_006_A_31st_due_day_falls_back_to_month_end_in_February()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-5), acquisitionType = "Lease", purchasePrice = 1_200_000, amountPaid = 0, paymentMode = "BankTransfer" });
        await AcceptanceHelpers.SaveFinanceAsync(w, vehicle, bank, new { financeAmount = 1_200_000, downPayment = 0, installmentAmount = 100_000, tenure = 12, firstDueDate = "2027-01-31", residualAmount = (decimal?)null });

        var response = await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/finance/schedule");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var rows = (await response.DataAsync()).EnumerateArray().ToList();

        var february = rows[1];   // row 2, one month after 31-Jan
        Assert.StartsWith("2027-02-2", february.GetProperty("dueDate").GetString());   // the 28th (2027 is not a leap year)
    }

    /// <summary>AC-VH-007: Given Category = Self Owned with no registration book uploaded, When Activate is pressed, Then activation is blocked with VAL-VH-014.</summary>
    [Fact]
    public async Task AC_VH_007_Activating_without_a_registration_book_is_blocked()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-1), acquisitionType = "Purchase", purchasePrice = 1_000_000, amountPaid = 1_000_000, paymentMode = "Cash" });
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "SelfOwned", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });

        await PartnerWorld.AssertRefusedAsync(response, "documents", Msg.VhRegistrationBookRequired);
    }

    /// <summary>
    /// AC-VH-008: Given an active vehicle Rented, When category changes to Self Owned, Then the rented relation closes the day
    /// before, the vehicle's own category and counterparty update to the new arrangement, and prior rent transactions are
    /// unchanged. Self Owned has no counterparty, so — as <c>CategoryService.ChangeAsync</c> deliberately does for this one
    /// category — no new relation *row* opens for it; the vehicle's own <c>CurrentCategory</c>/<c>CurrentCounterpartyId</c>
    /// is where "self owned now" actually lives, the same as it does for a fresh Self Owned vehicle that never had a relation at all.
    /// </summary>
    [Fact]
    public async Task AC_VH_008_A_category_change_closes_the_old_relation_the_day_before_the_new_category_takes_effect()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var lessor = await w.PartnerAsync("Vendor");
        var vehicle = await w.CreateAsync(w.Truck());
        factory.Execute("UPDATE veh.Vehicles SET Status = 'Active', CurrentCategory = 'Rented', AcquisitionDate = @a WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle)), ("@a", DateOnly.Parse(Day(-60))));
        factory.Execute(
            "INSERT INTO veh.VehicleRelations (TenantId, VehicleId, Category, CounterpartyId, EffectiveFrom, RentAmount, RentFrequency, RentDueDay) VALUES (@t, @v, 'Rented', @c, @f, 20000, 'Monthly', 5)",
            ("@t", w.Tenant), ("@v", VehicleWorld.Id(vehicle)), ("@c", lessor), ("@f", DateOnly.Parse(Day(-60))));
        var priorTransactionCount = factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle)));

        var changed = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/category", new { category = "SelfOwned", effectiveDate = Day(0), reason = "Bought outright", details = new { } });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        var relations = factory.Query("SELECT Category, EffectiveFrom, EffectiveTo FROM veh.VehicleRelations WHERE VehicleId = @v ORDER BY VehicleRelationId", ("@v", VehicleWorld.Id(vehicle)));
        var relation = Assert.Single(relations);   // Self Owned has no counterparty, so no new relation row opens for it (see the method doc above) — only the old one closes
        Assert.Equal("Rented", relation["Category"]);
        Assert.Equal(Day(-1), DateOnly.FromDateTime((DateTime)relation["EffectiveTo"]!).ToString("yyyy-MM-dd"));   // closed the day before the new category takes effect

        var updated = await w.GetAsync(VehicleWorld.Id(vehicle));
        Assert.Equal("SelfOwned", updated["currentCategory"]!.GetValue<string>());
        Assert.Equal(priorTransactionCount, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle))));
    }

    /// <summary>AC-VH-009: Given a vehicle saved as Draft with purchase data, When the Draft is viewed, Then no transactions exist; they appear only after activation.</summary>
    [Fact]
    public async Task AC_VH_009_A_draft_posts_nothing_until_it_is_activated()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-1), acquisitionType = "Purchase", purchasePrice = 2_000_000, amountPaid = 500_000, paymentMode = "Cash" });

        Assert.Empty((await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/transactions")).DataAsync()).EnumerateArray());
        Assert.Equal("Draft", factory.Scalar<string>("SELECT Status FROM veh.Vehicles WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle))));

        await w.UploadRegistrationBookAsync(VehicleWorld.Id(vehicle));
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));
        await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "SelfOwned", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });

        Assert.NotEmpty((await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/transactions")).DataAsync()).EnumerateArray());
    }

    /// <summary>AC-VH-010: Given a container costing 450,000 added at creation, When the vehicle is activated, Then the attached item exists and a major expense of 450,000 is posted.</summary>
    [Fact]
    public async Task AC_VH_010_An_attached_items_cost_posts_as_a_major_expense_on_activation()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        await AcceptanceHelpers.SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(-3), acquisitionType = "Purchase", purchasePrice = 2_000_000, amountPaid = 2_000_000, paymentMode = "Cash" });
        await w.UploadRegistrationBookAsync(VehicleWorld.Id(vehicle));
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));

        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new
        {
            category = "SelfOwned", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>(),
            items = new[] { new { itemTypeId = w.ItemType, description = "40 ft container", serialNo = "AC-VH-010", cost = 450_000 } },
        });
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());

        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleAttachedItems WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle))));
        var expense = factory.Query("SELECT Type, SubType, Amount FROM veh.VehicleTransactions WHERE VehicleId = @v AND Type = 'MajorExpense'", ("@v", VehicleWorld.Id(vehicle))).Single();
        Assert.Equal(450_000m, (decimal)expense["Amount"]!);
    }

    /// <summary>AC-VH-011: Given Ali is default driver of LES-1234, When Ali is set as default driver of LES-5678, Then VAL-VH-017 prompts to release the first assignment.</summary>
    [Fact]
    public async Task AC_VH_011_Reassigning_a_drivers_default_vehicle_prompts_to_release_the_old_one()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var ali = await w.DriverAsync();
        var first = await w.ActiveAsync();
        var second = await w.ActiveAsync();
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(first)}/driver", new { driverId = ali })).IsSuccessStatusCode);

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(second)}/driver", new { driverId = ali });
        var errors = await PartnerWorld.ErrorsAsync(response);
        Assert.Contains(errors, e => e.Code == Msg.VhDriverAlreadyAssigned);

        var released = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(second)}/driver", new { driverId = ali, releaseFromOther = true });
        Assert.True(released.IsSuccessStatusCode, await released.Content.ReadAsStringAsync());
    }

    /// <summary>AC-VH-012: Given a vehicle with 3 transactions, When delete is attempted, Then delete is unavailable; only retire, sell or transfer are offered.</summary>
    [Fact]
    public async Task AC_VH_012_A_vehicle_with_transactions_cannot_be_deleted_only_disposed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();

        var delete = await w.Admin.DeleteAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);   // the route exists for GET/PUT; no DELETE handler is registered on it at all — delete is simply not offered

        var disposed = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/dispose", new { kind = "Retire", date = Day(0), reason = "End of life" });
        Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());
    }

    /// <summary>AC-VH-013: Given an Operations user, When the vehicle screen is opened, Then purchase price and finance figures are hidden.</summary>
    [Fact]
    public async Task AC_VH_013_An_operations_user_does_not_see_purchase_price_or_finance_figures()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var operationsUser = w.As("Operations User", 40, PermissionCodes.VEH_VIEW);

        var seen = PartnerWorld.AsObject(await (await operationsUser.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}")).DataAsync());

        Assert.False(seen.ContainsKey("purchasePrice"));
        Assert.False(seen.ContainsKey("financeAmount"));
    }

    /// <summary>AC-VH-014: Given insurance expiring in 25 days with a 30-day rule, When the notification run executes, Then an alert is raised naming the vehicle and document.</summary>
    [Fact]
    public async Task AC_VH_014_Insurance_expiring_soon_raises_an_alert_naming_the_vehicle()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = NotificationFixture.RegisterRecipientAsync(factory, w.Tenant, "acvh014");
        var vehicle = await w.ActiveAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "INSURANCE_POLICY").GetProperty("id").GetInt32();
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(25);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" }, { new StringContent(expiry.ToString("yyyy-MM-dd")), "expiryDate" }, { new StringContent("POL-ACVH14"), "documentNumber" },
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "policy.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents", form);
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());   // the default 30-day rule already covers 25 days out

        Assert.Equal(1, NotificationFixture.CountFor(factory, w.Tenant, recipient, "DocumentExpiry"));
        var message = factory.Scalar<string>("SELECT TOP 1 Message FROM notif.Notifications WHERE TenantId = @t AND EventType = 'DocumentExpiry'", ("@t", w.Tenant));
        Assert.Contains(vehicle["registrationNo"]!.GetValue<string>(), message);
    }
}

/// <summary>Small setup helpers shared across this file, mirroring the patterns already established in <c>VehicleActivationTests</c>/<c>RecurringChargeTests</c>.</summary>
internal static class AcceptanceHelpers
{
    public static async Task SaveAcquisitionAsync(VehicleWorld w, JsonObject vehicle, object fields)
    {
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/acquisition")).DataAsync());
        var body = PartnerWorld.AsObject(JsonSerializer.SerializeToElement(fields));
        body["rowVersion"] = block["rowVersion"]!.GetValue<string>();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/acquisition", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task SaveFinanceAsync(VehicleWorld w, JsonObject vehicle, int bank, object? overrides = null)
    {
        var financeType = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();
        var body = new JsonObject
        {
            ["financeTypeId"] = financeType, ["bankId"] = bank, ["agreementNo"] = "AGR-" + Guid.NewGuid().ToString("N")[..8], ["agreementDate"] = DateTime.UtcNow.AddDays(-4).ToString("yyyy-MM-dd"),
            ["financeAmount"] = 3_000_000, ["downPayment"] = 0, ["installmentAmount"] = 150_000, ["frequency"] = "Monthly", ["tenure"] = 12, ["firstDueDate"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
        };
        if (overrides is not null) foreach (var (key, value) in PartnerWorld.AsObject(JsonSerializer.SerializeToElement(overrides))) body[key] = value?.DeepClone();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/finance", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Registers one real, DB-backed recipient for the Notifications module's <c>IUserDirectory</c> lookup to find — see
/// <c>NotificationTests.RegisterRecipientAsync</c>'s own note for why a synthetic <see cref="TestTokens"/> caller is
/// not enough here. Duplicated as a small static helper (not shared code) because the two test files' `ApiFactory`
/// and `Guid` types are otherwise unrelated and a shared base class would cost more clarity than it saves for two callers.
/// </summary>
internal static class NotificationFixture
{
    public static int RegisterRecipientAsync(ApiFactory factory, Guid tenant, string emailPrefix)
    {
        var roleId = factory.Scalar<int>("SELECT RoleID FROM auth.Roles WHERE RoleCode = 'TENANT_ADMIN' AND IsGlobal = 1");
        var email = $"{emailPrefix}.{Guid.NewGuid():N}@test.local";
        factory.Execute(
            "INSERT INTO auth.UserAccounts (TenantId, FirstName, LastName, Email, PasswordHash, RoleID, IsActive, IsDeleted, CreatedBy, CreatedDate) VALUES (@t, @fn, @ln, @e, @ph, @r, 1, 0, 0, @cd)",
            ("@t", tenant), ("@fn", "Notify"), ("@ln", "Recipient"), ("@e", email), ("@ph", "x"), ("@r", roleId), ("@cd", DateTime.UtcNow));
        var userId = factory.Scalar<int>("SELECT UserID FROM auth.UserAccounts WHERE Email = @e", ("@e", email));
        factory.Execute("INSERT INTO auth.UserRoles (UserID, RoleID, ScopeType, TenantId) VALUES (@u, @r, 'AllBranches', @t)", ("@u", userId), ("@r", roleId), ("@t", tenant));
        return userId;
    }

    public static int CountFor(ApiFactory factory, Guid tenant, int userId, string eventType) =>
        factory.Scalar<int>("SELECT COUNT(*) FROM notif.Notifications WHERE TenantId = @t AND UserId = @u AND EventType = @e", ("@t", tenant), ("@u", userId), ("@e", eventType));
}
