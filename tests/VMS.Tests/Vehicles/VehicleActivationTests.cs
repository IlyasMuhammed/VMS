using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Shared.Vehicles;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>Says the documents module has been asked and the registration book is not on file (BR-VH-015).</summary>
public sealed class MissingBookCheck : IVehicleDocumentCheck
{
    public bool CanCheck => true;
    public Task<bool> HasRegistrationBookAsync(int vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public static WebApplicationFactory<Program> Host(ApiFactory factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IVehicleDocumentCheck, MissingBookCheck>()));
}

/// <summary>S2-VH-09, S2-VH-10, S3-FIN-06, S3-FIN-07: the activation checklist, the schedule, the opening postings, and the rule that activation is all or nothing.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleActivationTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    // ── Setting up ──────────────────────────────────────────────────────────────────

    private static async Task SaveAcquisitionAsync(VehicleWorld w, JsonObject vehicle, object fields)
    {
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition")).DataAsync());
        var body = PartnerWorld.AsObject(JsonSerializer.SerializeToElement(fields));
        body["rowVersion"] = block["rowVersion"]!.GetValue<string>();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A Draft acquired five days ago for 5,000,000 of which 2,000,000 was paid, with a registration cost of 45,000.</summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle)> DraftAsync(VehicleWorld? world = null, JsonObject? body = null, int seller = 0, decimal price = 5_000_000, decimal paid = 2_000_000)
    {
        var w = world ?? await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(body ?? w.Truck());
        await SaveAcquisitionAsync(w, vehicle, new
        {
            acquisitionDate = Day(5), acquisitionType = "Purchase", sellerId = seller == 0 ? (int?)null : seller, purchasePrice = price, amountPaid = paid,
            paymentMode = "BankTransfer", paymentReference = "TRX-7", registrationCost = 45_000
        });
        return (w, vehicle);
    }

    private static async Task<int> LeaseTypeAsync(VehicleWorld w) =>
        (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();

    private static async Task SaveFinanceAsync(VehicleWorld w, JsonObject vehicle, int bank, object? overrides = null)
    {
        var body = new JsonObject
        {
            ["financeTypeId"] = await LeaseTypeAsync(w), ["bankId"] = bank, ["agreementNo"] = "AGR-" + Guid.NewGuid().ToString("N")[..8], ["agreementDate"] = Day(4), ["financeAmount"] = 3_000_000,
            ["downPayment"] = 2_000_000, ["installmentAmount"] = 150_000, ["frequency"] = "Monthly", ["tenure"] = 3, ["firstDueDate"] = "2027-01-31", ["residualAmount"] = 100_000, ["securityDeposit"] = 50_000
        };
        if (overrides is not null) foreach (var (key, value) in PartnerWorld.AsObject(JsonSerializer.SerializeToElement(overrides))) body[key] = value?.DeepClone();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonObject> BodyAsync(VehicleWorld w, JsonObject vehicle, string category, object? details = null, object? dueDates = null, bool reassignCard = false, bool releaseDriver = false, object? items = null)
    {
        var fresh = await w.GetAsync(Id(vehicle));
        return new JsonObject
        {
            ["category"] = category, ["details"] = details is null ? new JsonObject() : JsonSerializer.SerializeToNode(details), ["rowVersion"] = fresh["rowVersion"]!.GetValue<string>(),
            ["dueDates"] = dueDates is null ? new JsonArray() : JsonSerializer.SerializeToNode(dueDates), ["items"] = items is null ? new JsonArray() : JsonSerializer.SerializeToNode(items), ["reassignFuelCard"] = reassignCard, ["releaseFromOther"] = releaseDriver
        };
    }

    private static Task<HttpResponseMessage> Activate(VehicleWorld w, JsonObject vehicle, JsonObject body, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activate", body);

    private static async Task<JsonObject> CheckAsync(VehicleWorld w, JsonObject vehicle, JsonObject body, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activation-check", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    private List<Dictionary<string, object?>> Ledger(JsonObject v) =>
        factory.Query("SELECT Type, SubType, Amount, TransactionDate, PartnerId, Reference, Source, IsSystemGenerated FROM veh.VehicleTransactions WHERE VehicleId = @v ORDER BY VehicleTransactionId", ("@v", Id(v))).ToList();

    private string Status(JsonObject v) => factory.Scalar<string>("SELECT Status FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(v)));

    // ── All or nothing (written first: the highest-risk task) ───────────────────────

    [Fact]
    public async Task A_failure_part_way_through_leaves_no_trace_of_the_activation()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var driver = await w.DriverAsync();

        // The vehicle takes a fuel card and a driver from two others, which are the first writes of the transaction.
        var cardHolder = await w.ActiveAsync(w.Truck());
        var cardEdit = await w.GetAsync(Id(cardHolder));
        var fuelCo = await w.PartnerAsync("FuelCardCompany");
        cardEdit["fuelCardCompanyId"] = fuelCo; cardEdit["fuelCardNumber"] = "PSO-ROLLBACK";
        Assert.True((await w.PutAsync(cardEdit)).IsSuccessStatusCode);
        var driverHolder = await w.ActiveAsync(w.Truck());
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(driverHolder)}/driver", new { driverId = driver })).IsSuccessStatusCode);

        var draftBody = w.Truck();
        draftBody["fuelCardCompanyId"] = fuelCo; draftBody["fuelCardNumber"] = "PSO-ROLLBACK"; draftBody["defaultDriverId"] = driver; draftBody["draftData"] = "{\"step\":3}";
        var (_, vehicle) = await DraftAsync(w, draftBody);
        await SaveFinanceAsync(w, vehicle, bank);
        await w.UploadRegistrationBookAsync(Id(vehicle));

        // Something the activation cannot know about: an open relation already sits on the Draft, so the last write collides.
        factory.Execute("INSERT INTO veh.VehicleRelations (TenantId, VehicleId, Category, CounterpartyId, EffectiveFrom) VALUES (@t, @v, 'BankLeased', @b, SYSDATETIME())", ("@t", w.Tenant), ("@v", Id(vehicle)), ("@b", bank));

        var response = await Activate(w, vehicle, await BodyAsync(w, vehicle, "BankLeased", reassignCard: true, releaseDriver: true,
            items: new[] { new { itemTypeId = w.ItemType, description = "40 ft container", serialNo = "ROLLBACK-1", cost = 450_000 } }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleAttachedItems WHERE VehicleId = @v", ("@v", Id(vehicle))));   // the items went with it

        Assert.Equal("Draft", Status(vehicle));
        Assert.Null(factory.Scalar<string?>("SELECT CurrentCategory FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal("{\"step\":3}", factory.Scalar<string?>("SELECT DraftData FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Empty(Ledger(vehicle));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleInstallments WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal("Draft", factory.Scalar<string>("SELECT Status FROM veh.VehicleFinanceAgreements WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleLifecycle WHERE VehicleId = @v AND EventType = 'StatusChange'", ("@v", Id(vehicle))));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE VehicleId = @v", ("@v", Id(vehicle))));

        // What it had already taken from the others is back: the card is still theirs, the driver is still theirs.
        Assert.Equal("PSO-ROLLBACK", factory.Scalar<string?>("SELECT FuelCardNumber FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(cardHolder))));
        Assert.Equal(driver, factory.Scalar<int?>("SELECT DefaultDriverId FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(driverHolder))));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE VehicleId = @v AND DriverId = @d AND EffectiveTo IS NULL", ("@v", Id(driverHolder)), ("@d", driver)));
    }

    [Fact]
    public async Task Two_activations_at_once_write_one_set_of_entries()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var (_, vehicle) = await DraftAsync(w);
        await w.UploadRegistrationBookAsync(Id(vehicle));
        var body = await BodyAsync(w, vehicle, "SelfOwned");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Activate(w, vehicle, (JsonObject)body.DeepClone())));
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, r.StatusCode.ToString()));
        Assert.Equal(3, Ledger(vehicle).Count);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleLifecycle WHERE VehicleId = @v AND EventType = 'StatusChange'", ("@v", Id(vehicle))));
    }

    // ── What activation writes ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_self_owned_vehicle_goes_into_the_fleet_and_its_ledger_opens_with_what_was_entered()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var seller = await w.PartnerAsync("Vendor");
        var draftBody = w.Truck(); draftBody["draftData"] = "{\"x\":1}";
        var (_, vehicle) = await DraftAsync(w, draftBody, seller);
        Assert.Empty(Ledger(vehicle));   // AC-VH-009
        await w.UploadRegistrationBookAsync(Id(vehicle));

        var response = await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var active = PartnerWorld.AsObject(await response.DataAsync());
        Assert.Equal("Active", active["status"]!.GetValue<string>());
        Assert.Equal("SelfOwned", active["currentCategory"]!.GetValue<string>());
        Assert.Null(active["draftData"]);

        var ledger = Ledger(vehicle);
        Assert.Equal(["Acquisition", "InitialPayment", "MajorExpense"], ledger.Select(r => (string)r["Type"]!));   // AC-VH-003
        Assert.Equal([5_000_000m, 2_000_000m, 45_000m], ledger.Select(r => (decimal)r["Amount"]!));
        Assert.Equal("Registration", ledger[2]["SubType"]);
        Assert.All(ledger, r =>
        {
            Assert.Equal(Day(5), ((DateTime)r["TransactionDate"]!).ToString("yyyy-MM-dd"));   // dated by the acquisition, not by the activation (BR-VH-012)
            Assert.Equal("VehicleCreation", r["Source"]);
            Assert.Equal(true, r["IsSystemGenerated"]);
            Assert.Equal(seller, r["PartnerId"]);
        });
        Assert.Equal($"Vehicle acquisition — {active["registrationNo"]!.GetValue<string>()}", ledger[0]["Reference"]);
        Assert.Equal("Initial payment — BankTransfer TRX-7", ledger[1]["Reference"]);
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleRelations WHERE VehicleId = @v", ("@v", Id(vehicle))));   // Self Owned has no relation
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleLifecycle WHERE VehicleId = @v AND EventType = 'StatusChange' AND ToStatus = 'Active'", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task A_bank_leased_vehicle_takes_its_bank_from_the_agreement_and_gets_its_schedule_and_deposit()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var (_, vehicle) = await DraftAsync(w);
        await SaveFinanceAsync(w, vehicle, bank);
        await w.UploadRegistrationBookAsync(Id(vehicle));

        var response = await Activate(w, vehicle, await BodyAsync(w, vehicle, "BankLeased"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var active = PartnerWorld.AsObject(await response.DataAsync());
        Assert.Equal(bank, active["currentCounterparty"]!["id"]!.GetValue<int>());
        Assert.Equal(bank, factory.Scalar<int>("SELECT CounterpartyId FROM veh.VehicleRelations WHERE VehicleId = @v AND EffectiveTo IS NULL", ("@v", Id(vehicle))));
        Assert.Equal(Day(5), factory.Scalar<DateTime>("SELECT EffectiveFrom FROM veh.VehicleRelations WHERE VehicleId = @v", ("@v", Id(vehicle))).ToString("yyyy-MM-dd"));

        // The lease itself posts nothing; the deposit goes to the bank; the down payment is the initial payment, counted once.
        var ledger = Ledger(vehicle);
        Assert.Equal(["Acquisition", "InitialPayment", "MajorExpense", "Deposit"], ledger.Select(r => (string)r["Type"]!));
        Assert.Equal("DownPayment", ledger[1]["SubType"]);
        Assert.Equal(50_000m, ledger[3]["Amount"]);
        Assert.Equal(bank, ledger[3]["PartnerId"]);
        Assert.Equal("Security deposit", ledger[3]["Reference"]);

        Assert.Equal("Active", factory.Scalar<string>("SELECT Status FROM veh.VehicleFinanceAgreements WHERE VehicleId = @v", ("@v", Id(vehicle))));
        var rows = factory.Query("SELECT InstallmentNo, DueDate, ExpectedAmount, PaidAmount, IsResidual, Status FROM veh.VehicleInstallments WHERE VehicleId = @v ORDER BY InstallmentNo", ("@v", Id(vehicle))).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(["2027-01-31", "2027-02-28", "2027-03-31", "2027-03-31"], rows.Select(r => ((DateTime)r["DueDate"]!).ToString("yyyy-MM-dd")));
        Assert.Equal([150_000m, 150_000m, 150_000m, 100_000m], rows.Select(r => (decimal)r["ExpectedAmount"]!));
        Assert.Equal([false, false, false, true], rows.Select(r => (bool)r["IsResidual"]!));
        Assert.All(rows, r => { Assert.Equal(0m, r["PaidAmount"]); Assert.Equal("Pending", r["Status"]); });

        // And the schedule is what the installment API now lists.
        var listed = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/installments")).DataAsync()).EnumerateArray().ToList();
        Assert.Equal(4, listed.Count);
    }

    [Fact]
    public async Task A_rented_vehicle_opens_its_relation_and_posts_the_deposit_to_the_lessor()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var lessor = await w.PartnerAsync("Vendor");
        var (_, vehicle) = await DraftAsync(w, price: 100_000, paid: 100_000);

        var response = await Activate(w, vehicle, await BodyAsync(w, vehicle, "Rented", new { counterpartyId = lessor, rentAmount = 80_000, rentFrequency = "Monthly", rentDueDay = 5, securityDeposit = 30_000 }));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(lessor, factory.Scalar<int>("SELECT CounterpartyId FROM veh.VehicleRelations WHERE VehicleId = @v AND EffectiveTo IS NULL", ("@v", Id(vehicle))));
        var deposit = Ledger(vehicle).Single(r => (string)r["Type"]! == "Deposit");
        Assert.Equal(30_000m, deposit["Amount"]);
        Assert.Equal(lessor, deposit["PartnerId"]);
    }

    [Fact]
    public async Task The_default_driver_and_fuel_card_of_a_draft_are_taken_only_when_the_person_agrees()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var fuelCo = await w.PartnerAsync("FuelCardCompany");
        var cardHolder = await w.ActiveAsync(w.Truck());
        var edit = await w.GetAsync(Id(cardHolder)); edit["fuelCardCompanyId"] = fuelCo; edit["fuelCardNumber"] = "PSO-ACT-1";
        Assert.True((await w.PutAsync(edit)).IsSuccessStatusCode);
        var driverHolder = await w.ActiveAsync(w.Truck());
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(driverHolder)}/driver", new { driverId = driver })).IsSuccessStatusCode);

        var body = w.Truck(); body["fuelCardCompanyId"] = fuelCo; body["fuelCardNumber"] = "PSO-ACT-1"; body["defaultDriverId"] = driver;
        var (_, vehicle) = await DraftAsync(w, body);
        await w.UploadRegistrationBookAsync(Id(vehicle));

        var asked = await PartnerWorld.ErrorsAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned")));
        Assert.Contains(asked, e => e is { Field: "fuelCardNumber", Code: Msg.VhFuelCardInUse });
        Assert.Contains(asked, e => e is { Field: "defaultDriverId", Code: Msg.VhDriverAlreadyAssigned });
        Assert.Equal("Draft", Status(vehicle));

        var agreed = await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned", reassignCard: true, releaseDriver: true));
        Assert.True(agreed.IsSuccessStatusCode, await agreed.Content.ReadAsStringAsync());
        Assert.Equal(driver, factory.Scalar<int>("SELECT DefaultDriverId FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.DriverAssignments WHERE VehicleId = @v AND DriverId = @d AND EffectiveTo IS NULL", ("@v", Id(vehicle)), ("@d", driver)));
        Assert.Null(factory.Scalar<int?>("SELECT DefaultDriverId FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(driverHolder))));
        Assert.Null(factory.Scalar<string?>("SELECT FuelCardNumber FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(cardHolder))));
        Assert.Equal("PSO-ACT-1", factory.Scalar<string>("SELECT FuelCardNumber FROM veh.Vehicles WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    // ── The schedule ────────────────────────────────────────────────────────────────

    private async Task<List<(int No, string Due, decimal Amount, bool Residual)>> PreviewAsync(VehicleWorld w, JsonObject vehicle)
    {
        var response = await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/finance/schedule");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).EnumerateArray()
            .Select(r => (r.GetProperty("installmentNo").GetInt32(), r.GetProperty("dueDate").GetString()!, r.GetProperty("amount").GetDecimal(), r.GetProperty("isResidual").GetBoolean())).ToList();
    }

    [Fact]
    public async Task Due_dates_step_by_the_frequency_and_the_end_of_a_short_month_never_moves_the_later_dates()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var (_, vehicle) = await DraftAsync(w);
        Assert.Empty(await PreviewAsync(w, vehicle));   // no agreement, no schedule

        await SaveFinanceAsync(w, vehicle, bank, new { frequency = "Monthly", tenure = 5, firstDueDate = "2027-01-31", residualAmount = (decimal?)null });
        Assert.Equal(["2027-01-31", "2027-02-28", "2027-03-31", "2027-04-30", "2027-05-31"], (await PreviewAsync(w, vehicle)).Select(r => r.Due));

        var current = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/finance")).DataAsync());
        async Task Resave(object terms)
        {
            var body = PartnerWorld.AsObject(JsonSerializer.SerializeToElement(terms));
            body["financeTypeId"] = current["financeTypeId"]!.DeepClone(); body["bankId"] = bank; body["agreementNo"] = current["agreementNo"]!.DeepClone(); body["agreementDate"] = current["agreementDate"]!.DeepClone();
            body["financeAmount"] = 3_000_000; body["downPayment"] = 2_000_000; body["installmentAmount"] = 150_000;
            body["rowVersion"] = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/finance")).DataAsync())["rowVersion"]!.GetValue<string>();
            var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", body);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        await Resave(new { frequency = "Quarterly", tenure = 4, firstDueDate = "2027-01-31" });
        Assert.Equal(["2027-01-31", "2027-04-30", "2027-07-31", "2027-10-31"], (await PreviewAsync(w, vehicle)).Select(r => r.Due));

        await Resave(new { frequency = "HalfYearly", tenure = 3, firstDueDate = "2027-08-31", residualAmount = 100_000 });
        var half = await PreviewAsync(w, vehicle);
        Assert.Equal(["2027-08-31", "2028-02-29", "2028-08-31", "2028-08-31"], half.Select(r => r.Due));   // a leap February
        Assert.Equal([1, 2, 3, 4], half.Select(r => r.No));
        Assert.Equal([150_000m, 150_000m, 150_000m, 100_000m], half.Select(r => r.Amount));   // the residual is a final row, due with the last installment
        Assert.Equal([false, false, false, true], half.Select(r => r.Residual));

        // Tenure 1 and the longest tenure.
        await Resave(new { frequency = "Monthly", tenure = 1, firstDueDate = "2027-05-15" });
        Assert.Equal(["2027-05-15"], (await PreviewAsync(w, vehicle)).Select(r => r.Due));
        await Resave(new { frequency = "Monthly", tenure = 120, firstDueDate = "2027-05-15" });
        var longest = await PreviewAsync(w, vehicle);
        Assert.Equal(120, longest.Count);
        Assert.Equal("2037-04-15", longest[^1].Due);

        // Nothing has been written by looking.
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleInstallments WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task Due_dates_the_person_corrected_are_kept_and_dates_that_go_backwards_are_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var (_, vehicle) = await DraftAsync(w);
        await SaveFinanceAsync(w, vehicle, bank, new { residualAmount = (decimal?)null });
        await w.UploadRegistrationBookAsync(Id(vehicle));

        var backwards = await BodyAsync(w, vehicle, "BankLeased", dueDates: new[] { new { installmentNo = 2, dueDate = "2027-01-15" } });
        var check = await CheckAsync(w, vehicle, backwards);
        Assert.False(check["canActivate"]!.GetValue<bool>());
        Assert.Contains(check["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "schedule" && !i["ok"]!.GetValue<bool>());
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, backwards), "dueDates", Msg.VhInstallmentOrder);

        var beforeAgreement = await BodyAsync(w, vehicle, "BankLeased", dueDates: new[] { new { installmentNo = 1, dueDate = Day(30) } });
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, beforeAgreement), "dueDates", Msg.VhInstallmentOrder);

        var unknown = await BodyAsync(w, vehicle, "BankLeased", dueDates: new[] { new { installmentNo = 9, dueDate = "2027-06-01" } });
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, unknown), "dueDates", Msg.Invalid);

        var irregular = await BodyAsync(w, vehicle, "BankLeased", dueDates: new[] { new { installmentNo = 2, dueDate = "2027-03-10" }, new { installmentNo = 3, dueDate = "2027-04-02" } });
        var preview = await CheckAsync(w, vehicle, irregular);
        Assert.Equal(["2027-01-31", "2027-03-10", "2027-04-02"], preview["schedule"]!.AsArray().Select(r => r!["dueDate"]!.GetValue<string>()));
        Assert.True((await Activate(w, vehicle, irregular)).IsSuccessStatusCode);
        Assert.Equal(["2027-01-31", "2027-03-10", "2027-04-02"],
            factory.Query("SELECT DueDate FROM veh.VehicleInstallments WHERE VehicleId = @v ORDER BY InstallmentNo", ("@v", Id(vehicle))).Select(r => ((DateTime)r["DueDate"]!).ToString("yyyy-MM-dd")));
    }

    // ── The checklist ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_checklist_says_what_is_missing_and_writes_nothing()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());   // nothing entered but the vehicle itself

        var empty = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, ""));
        Assert.False(empty["canActivate"]!.GetValue<bool>());
        var failing = empty["items"]!.AsArray().Where(i => !i!["ok"]!.GetValue<bool>()).Select(i => i!["code"]!.GetValue<string>()).ToList();
        Assert.Contains("category", failing);
        Assert.Contains("acquisition", failing);
        Assert.Empty(empty["postings"]!.AsArray());

        var rentedNoLessor = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "Rented", new { rentAmount = 1, rentFrequency = "Weekly" }));
        Assert.Contains(rentedNoLessor["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "category" && i["message"]!.GetValue<string>().Contains("Lessor"));

        // Fill in what was missing and it can be activated, and the postings say what would be written.
        await SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(3), acquisitionType = "Purchase", purchasePrice = 900_000, amountPaid = 900_000, paymentMode = "Cash" });
        await w.UploadRegistrationBookAsync(Id(vehicle));
        var ready = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"));
        Assert.True(ready["canActivate"]!.GetValue<bool>(), ready.ToJsonString());
        Assert.Equal(["Acquisition", "InitialPayment"], ready["postings"]!.AsArray().Select(p => p!["type"]!.GetValue<string>()));
        Assert.Equal(900_000m, ready["postings"]![0]!["amount"]!.GetValue<decimal>());

        Assert.Equal("Draft", Status(vehicle));
        Assert.Empty(Ledger(vehicle));
    }

    [Fact]
    public async Task A_price_is_needed_for_a_self_owned_or_bank_leased_vehicle_but_not_for_the_others()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        await SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(3), acquisitionType = "Rent" });
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned")), "purchasePrice", Msg.Required);

        var lessor = await w.PartnerAsync("Vendor");
        var rented = await Activate(w, vehicle, await BodyAsync(w, vehicle, "Rented", new { counterpartyId = lessor, rentAmount = 50_000, rentFrequency = "Weekly" }));
        Assert.True(rented.IsSuccessStatusCode, await rented.Content.ReadAsStringAsync());
        Assert.Empty(Ledger(vehicle));   // nothing was bought, so nothing is posted
    }

    [Fact]
    public async Task The_finance_agreement_and_the_category_must_go_together_and_agree_with_the_acquisition()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var bank = await w.PartnerAsync("Bank");
        var otherBank = await w.PartnerAsync("Bank");
        var (_, vehicle) = await DraftAsync(w);

        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "BankLeased", new { counterpartyId = bank })), "finance", Msg.VhBankLeasedNeedsFinance);

        await SaveFinanceAsync(w, vehicle, bank);
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned")), "category", Msg.VhFinanceNeedsBankLeased);
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "BankLeased", new { counterpartyId = otherBank })), "details.counterpartyId", Msg.VhBankIsAgreementBank);

        // The amount paid was changed after the agreement was saved: the two no longer agree (BR-VH-009).
        await SaveAcquisitionAsync(w, vehicle, new { acquisitionDate = Day(5), acquisitionType = "Purchase", purchasePrice = 5_000_000, amountPaid = 1_500_000, paymentMode = "Cash" });
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "BankLeased")), "downPayment", Msg.VhDownPaymentMismatch);
        Assert.Equal("Draft", Status(vehicle));
    }

    [Fact]
    public async Task The_registration_book_is_required_for_a_vehicle_we_own_or_lease_and_only_a_warning_for_a_rented_one()
    {
        var w = await VehicleWorld.CreateAsync(factory, MissingBookCheck.Host(factory));
        var (_, owned) = await DraftAsync(w);
        var check = await CheckAsync(w, owned, await BodyAsync(w, owned, "SelfOwned"));
        Assert.Contains(check["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "registrationBook" && !i["ok"]!.GetValue<bool>() && i["blocking"]!.GetValue<bool>());
        await PartnerWorld.AssertRefusedAsync(await Activate(w, owned, await BodyAsync(w, owned, "SelfOwned")), "documents", Msg.VhRegistrationBookRequired);   // AC-VH-007
        Assert.Equal("Draft", Status(owned));

        var lessor = await w.PartnerAsync("Vendor");
        var (_, rented) = await DraftAsync(w, price: 1000, paid: 1000);
        var rentedBody = await BodyAsync(w, rented, "Rented", new { counterpartyId = lessor, rentAmount = 1000, rentFrequency = "Weekly" });
        var warn = await CheckAsync(w, rented, rentedBody);
        Assert.True(warn["canActivate"]!.GetValue<bool>());
        Assert.Contains(warn["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "registrationBook" && !i["ok"]!.GetValue<bool>() && !i["blocking"]!.GetValue<bool>());
        Assert.True((await Activate(w, rented, rentedBody)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Once_the_registration_book_is_on_file_the_checklist_says_so_and_a_self_owned_vehicle_activates()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var (_, vehicle) = await DraftAsync(w);
        var before = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"));
        var missing = before["items"]!.AsArray().Single(i => i!["code"]!.GetValue<string>() == "registrationBook")!;
        Assert.False(missing["ok"]!.GetValue<bool>());
        Assert.False(before["canActivate"]!.GetValue<bool>());

        await w.UploadRegistrationBookAsync(Id(vehicle));

        var after = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"));
        var present = after["items"]!.AsArray().Single(i => i!["code"]!.GetValue<string>() == "registrationBook")!;
        Assert.True(present["ok"]!.GetValue<bool>());
        Assert.True((await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"))).IsSuccessStatusCode);
    }

    // ── Items entered on the Draft (step 4) ──────────────────────────────────────────

    [Fact]
    public async Task The_items_of_a_draft_are_attached_and_their_costs_posted_when_it_is_activated()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var maker = await w.PartnerAsync("BodyMaker");
        var (_, vehicle) = await DraftAsync(w);
        await w.UploadRegistrationBookAsync(Id(vehicle));
        var items = new object[]
        {
            new { itemTypeId = w.ItemType, description = "40 ft container", serialNo = "msku-450", supplierId = maker, cost = 450_000, condition = "New" },
            new { itemTypeId = w.ItemType, description = "Spare container", installationDate = Day(3) }
        };

        var check = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned", items: items));
        Assert.True(check["canActivate"]!.GetValue<bool>(), check.ToJsonString());
        Assert.Contains(check["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "items" && i["ok"]!.GetValue<bool>());
        Assert.Equal(4, check["postings"]!.AsArray().Count);   // acquisition, initial payment, registration, and the container
        Assert.Empty(factory.Query("SELECT 1 FROM veh.VehicleAttachedItems WHERE VehicleId = @v", ("@v", Id(vehicle))));   // looking writes nothing

        var response = await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned", items: items));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var rows = factory.Query("SELECT Description, SerialNo, InstallationDate, Status, Cost, SupplierId FROM veh.VehicleAttachedItems WHERE VehicleId = @v ORDER BY VehicleAttachedItemId", ("@v", Id(vehicle))).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("MSKU-450", rows[0]["SerialNo"]);   // serial numbers are kept in capitals
        Assert.Equal(Day(5), ((DateTime)rows[0]["InstallationDate"]!).ToString("yyyy-MM-dd"));   // defaults to the acquisition date
        Assert.Equal(Day(3), ((DateTime)rows[1]["InstallationDate"]!).ToString("yyyy-MM-dd"));
        Assert.All(rows, r => Assert.Equal("Attached", r["Status"]));
        Assert.Equal(450_000m, rows[0]["Cost"]);

        var expense = Ledger(vehicle).Single(r => (string)r["Type"]! == "MajorExpense" && (string?)r["SubType"] == "Container");   // AC-VH-010
        Assert.Equal(450_000m, expense["Amount"]);
        Assert.Equal(maker, expense["PartnerId"]);
        Assert.Equal("VehicleCreation", expense["Source"]);
        Assert.Equal("Container — 40 ft container", expense["Reference"]);
        Assert.Equal(Day(5), ((DateTime)expense["TransactionDate"]!).ToString("yyyy-MM-dd"));

        var listed = (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/items")).DataAsync()).EnumerateArray().ToList();
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public async Task An_item_that_breaks_a_rule_stops_the_activation_and_says_which_one()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driver = await w.DriverAsync();
        var holder = await w.ActiveAsync(w.Truck());
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(holder)}/items", new { itemTypeId = w.ItemType, description = "Taken", serialNo = "TAKEN-1", installationDate = Day(1) })).IsSuccessStatusCode);
        var (_, vehicle) = await DraftAsync(w);

        var items = new object[]
        {
            new { itemTypeId = 999_999, description = "", serialNo = "OK-1" },
            new { itemTypeId = w.ItemType, description = "Duplicate of another vehicle", serialNo = "taken-1" },
            new { itemTypeId = w.ItemType, description = "Before acquisition", installationDate = Day(30) },
            new { itemTypeId = w.ItemType, description = "In the future", installationDate = Day(-2) },
            new { itemTypeId = w.ItemType, description = "Driver as supplier", supplierId = driver },
            new { itemTypeId = w.ItemType, description = "Same serial twice", serialNo = "OK-1" },
            new { itemTypeId = w.ItemType, description = "Negative cost", cost = -5 }
        };
        var errors = await PartnerWorld.ErrorsAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned", items: items)));
        Assert.Contains(errors, e => e is { Field: "items[0].itemTypeId", Code: Msg.Invalid });
        Assert.Contains(errors, e => e is { Field: "items[0].description", Code: Msg.Required });
        Assert.Contains(errors, e => e is { Field: "items[1].serialNo", Code: Msg.VhChassisOrEngineDuplicate });
        Assert.Contains(errors, e => e is { Field: "items[2].installationDate", Code: Msg.VhItemBeforeAcquisition });
        Assert.Contains(errors, e => e is { Field: "items[3].installationDate", Code: Msg.NotFuture });
        Assert.Contains(errors, e => e is { Field: "items[4].supplierId", Code: Msg.VhCounterpartyLacksRole });
        Assert.Contains(errors, e => e is { Field: "items[5].serialNo", Code: Msg.ListedTwice });
        Assert.Contains(errors, e => e is { Field: "items[6].cost", Code: Msg.Invalid });

        var check = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned", items: items));
        Assert.False(check["canActivate"]!.GetValue<bool>());
        Assert.Contains(check["items"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "items" && !i["ok"]!.GetValue<bool>());

        Assert.Equal("Draft", Status(vehicle));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleAttachedItems WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Empty(Ledger(vehicle));
    }

    [Fact]
    public async Task An_item_cost_entered_by_someone_who_may_not_see_cost_is_ignored_and_posts_nothing()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var (_, vehicle) = await DraftAsync(w);
        await w.UploadRegistrationBookAsync(Id(vehicle));
        var activator = w.As("Activator", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ACTIVATE);
        var body = await BodyAsync(w, vehicle, "SelfOwned", items: new[] { new { itemTypeId = w.ItemType, description = "AC unit", cost = 99_999 } });
        Assert.True((await Activate(w, vehicle, body, activator)).IsSuccessStatusCode);

        Assert.Null(factory.Scalar<decimal?>("SELECT Cost FROM veh.VehicleAttachedItems WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.DoesNotContain(Ledger(vehicle), r => (string?)r["SubType"] == "Container");
        Assert.Equal(3, Ledger(vehicle).Count);   // only the acquisition, the initial payment and the registration
    }

    // ── Who, and when ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_a_draft_can_be_activated_with_the_row_version_it_was_read_at_and_only_by_someone_who_may()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var (_, vehicle) = await DraftAsync(w);
        await w.UploadRegistrationBookAsync(Id(vehicle));
        var body = await BodyAsync(w, vehicle, "SelfOwned");

        var noVersion = (JsonObject)body.DeepClone(); noVersion["rowVersion"] = "";
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, noVersion), "rowVersion", Msg.Required);

        var manager = w.As("Fleet Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EDIT);
        Assert.Equal(HttpStatusCode.Forbidden, (await Activate(w, vehicle, body, manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.As("Viewer", 6, PermissionCodes.VEH_VIEW).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activation-check", body)).StatusCode);

        // Someone edited the vehicle after this copy was read.
        var edit = await w.GetAsync(Id(vehicle)); edit["remarks"] = "changed";
        Assert.True((await w.PutAsync(edit)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Activate(w, vehicle, body)).StatusCode);
        Assert.Equal("Draft", Status(vehicle));

        var fresh = await BodyAsync(w, vehicle, "SelfOwned");
        Assert.True((await Activate(w, vehicle, fresh)).IsSuccessStatusCode);
        await PartnerWorld.AssertRefusedAsync(await Activate(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned")), "status", Msg.VhWrongStatus);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activation-check", fresh), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task Someone_who_may_activate_but_not_see_cost_gets_the_postings_without_their_amounts()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var (_, vehicle) = await DraftAsync(w);
        var activator = w.As("Activator", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ACTIVATE);
        var check = await CheckAsync(w, vehicle, await BodyAsync(w, vehicle, "SelfOwned"), activator);
        var postings = check["postings"]!.AsArray();
        Assert.Equal(3, postings.Count);
        Assert.All(postings, p => Assert.False(p!.AsObject().ContainsKey("amount")));
    }

    // ── Item costs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_attached_items_cost_is_posted_as_a_major_expense_on_the_day_it_was_installed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var body = await w.PartnerAsync("BodyMaker");
        var vehicle = await w.ActiveAsync(acquired: Day(30));

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/items", new { itemTypeId = w.ItemType, description = "40 ft container", serialNo = "MSKU-45", installationDate = Day(10), cost = 450_000, supplierId = body });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var entry = Ledger(vehicle).Single();   // AC-VH-010
        Assert.Equal("MajorExpense", entry["Type"]);
        Assert.Equal("Container", entry["SubType"]);
        Assert.Equal(450_000m, entry["Amount"]);
        Assert.Equal(body, entry["PartnerId"]);
        Assert.Equal("Container — 40 ft container", entry["Reference"]);
        Assert.Equal(Day(10), ((DateTime)entry["TransactionDate"]!).ToString("yyyy-MM-dd"));

        // No cost, no expense; and a cost the caller may not enter is ignored, so nothing is posted for it either.
        Assert.True((await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/items", new { itemTypeId = w.ItemType, description = "Spare", installationDate = Day(9) })).IsSuccessStatusCode);
        var manager = w.As("Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ITEM_MANAGE);
        Assert.True((await manager.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/items", new { itemTypeId = w.ItemType, description = "AC unit", installationDate = Day(8), cost = 99_999 })).IsSuccessStatusCode);
        Assert.Single(Ledger(vehicle));
    }
}
