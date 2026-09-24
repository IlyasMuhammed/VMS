using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S3-FIN-09: recording a payment against an installment. The schedule is generated at activation (S3-FIN-06), which is not built yet, so these tests write the schedule directly.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleInstallmentTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    /// <summary>A vehicle in the fleet with an active agreement and three installments of 150,000 followed by a residual of 100,000.</summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle, int Bank, int AgreementId)> LeasedAsync(VehicleWorld? world = null)
    {
        var w = world ?? await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition")).DataAsync());
        Assert.True((await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", new
        {
            acquisitionDate = Day(60), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer", rowVersion = block["rowVersion"]!.GetValue<string>()
        })).IsSuccessStatusCode);

        var bank = await w.PartnerAsync("Bank");
        var type = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();
        var saved = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", new
        {
            financeTypeId = type, bankId = bank, agreementNo = "AGR-" + Guid.NewGuid().ToString("N")[..8], agreementDate = Day(59), financeAmount = 3_000_000, downPayment = 2_000_000,
            installmentAmount = 150_000, frequency = "Monthly", tenure = 3, firstDueDate = Day(30), residualAmount = 100_000
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var agreementId = PartnerWorld.AsObject(await saved.DataAsync())["id"]!.GetValue<int>();

        // What activation will do (S2-VH-09, S3-FIN-06): the vehicle and the agreement go live and the schedule is written.
        factory.Execute("UPDATE veh.Vehicles SET Status = 'Active', CurrentCategory = 'BankLeased' WHERE VehicleId = @v", ("@v", Id(vehicle)));
        factory.Execute("UPDATE veh.VehicleFinanceAgreements SET Status = 'Active' WHERE VehicleFinanceAgreementId = @a", ("@a", agreementId));
        for (var n = 1; n <= 4; n++)
            factory.Execute(
                "INSERT INTO veh.VehicleInstallments (TenantId, VehicleId, VehicleFinanceAgreementId, InstallmentNo, DueDate, ExpectedAmount, PaidAmount, IsResidual, Status) VALUES (@t, @v, @a, @n, DATEADD(day, @d, CAST(SYSDATETIME() AS date)), @e, 0, @r, 'Pending')",
                ("@t", w.Tenant), ("@v", Id(vehicle)), ("@a", agreementId), ("@n", n), ("@d", -30 + (n - 1) * 30), ("@e", n == 4 ? 100_000m : 150_000m), ("@r", n == 4));
        return (w, vehicle, bank, agreementId);
    }

    private static async Task<List<JsonObject>> Schedule(VehicleWorld w, JsonObject vehicle, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/installments");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
    }

    private static Task<HttpResponseMessage> Pay(VehicleWorld w, JsonObject vehicle, JsonObject installment, object fields, HttpClient? client = null, string? rowVersion = null)
    {
        var body = PartnerWorld.AsObject(JsonSerializer.SerializeToElement(fields));
        body["rowVersion"] = rowVersion ?? installment["rowVersion"]!.GetValue<string>();
        return (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/installments/{installment["id"]!.GetValue<int>()}/payments", body);
    }

    [Fact]
    public async Task The_schedule_is_listed_in_due_order_and_is_empty_while_the_agreement_is_a_draft()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());
        Assert.Empty(await Schedule(w, draft));

        var (world, vehicle, _, _) = await LeasedAsync();
        var schedule = await Schedule(world, vehicle);
        Assert.Equal([1, 2, 3, 4], schedule.Select(s => s["installmentNo"]!.GetValue<int>()));
        Assert.True(schedule[3]["isResidual"]!.GetValue<bool>());
        Assert.All(schedule, s => Assert.Equal("Pending", s["status"]!.GetValue<string>()));
        Assert.Equal(150_000m, schedule[0]["remainingAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Paying_an_installment_marks_it_paid_and_posts_one_ledger_entry_to_the_bank()
    {
        var (w, vehicle, bank, _) = await LeasedAsync();
        var first = (await Schedule(w, vehicle))[0];

        var response = await Pay(w, vehicle, first, new { amount = 150_000, paidOn = Day(2), paymentMode = "Cheque", reference = "CH-4411" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = PartnerWorld.AsObject(await response.DataAsync());
        Assert.Equal("Paid", result["installment"]!["status"]!.GetValue<string>());
        Assert.Equal(0m, result["installment"]!["remainingAmount"]!.GetValue<decimal>());
        Assert.False(result["agreementSettled"]!.GetValue<bool>());

        var row = factory.Query("SELECT Type, Amount, TransactionDate, PartnerId, Reference, Source, IsSystemGenerated FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))).Single();
        Assert.Equal("Installment", row["Type"]);
        Assert.Equal(150_000m, row["Amount"]);
        Assert.Equal(bank, row["PartnerId"]);
        Assert.Equal("Manual", row["Source"]);
        Assert.Equal(false, row["IsSystemGenerated"]);
        Assert.Contains("Installment 1", (string)row["Reference"]!);
        Assert.Contains("Cheque CH-4411", (string)row["Reference"]!);
        Assert.Equal(Day(2), ((DateTime)row["TransactionDate"]!).ToString("yyyy-MM-dd"));
        Assert.Equal(result["transactionId"]!.GetValue<int>(), factory.Scalar<int>("SELECT VehicleTransactionId FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task A_part_payment_leaves_the_rest_due_and_a_payment_can_never_exceed_what_is_due()
    {
        var (w, vehicle, _, _) = await LeasedAsync();
        var first = (await Schedule(w, vehicle))[0];

        var part = await Pay(w, vehicle, first, new { amount = 100_000, paidOn = Day(3) });
        Assert.True(part.IsSuccessStatusCode, await part.Content.ReadAsStringAsync());
        var afterPart = (await Schedule(w, vehicle))[0];
        Assert.Equal("PartiallyPaid", afterPart["status"]!.GetValue<string>());
        Assert.Equal(100_000m, afterPart["paidAmount"]!.GetValue<decimal>());
        Assert.Equal(50_000m, afterPart["remainingAmount"]!.GetValue<decimal>());

        var over = await PartnerWorld.ErrorsAsync(await Pay(w, vehicle, afterPart, new { amount = 50_000.01m, paidOn = Day(2) }));
        Assert.Contains(over, e => e is { Field: "amount", Code: Msg.Max } && e.Message.Contains("50,000.00"));

        Assert.True((await Pay(w, vehicle, afterPart, new { amount = 50_000, paidOn = Day(2) })).IsSuccessStatusCode);
        Assert.Equal("Paid", (await Schedule(w, vehicle))[0]["status"]!.GetValue<string>());
        Assert.Equal(150_000m, factory.Scalar<decimal>("SELECT SUM(Amount) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal(2, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task Paying_the_last_thing_due_settles_the_agreement_and_it_takes_no_more_payments()
    {
        var (w, vehicle, _, agreementId) = await LeasedAsync();
        JsonObject? last = null;
        foreach (var expected in new[] { 150_000, 150_000, 150_000, 100_000 })
        {
            var next = (await Schedule(w, vehicle)).First(s => s["status"]!.GetValue<string>() != "Paid");
            var response = await Pay(w, vehicle, next, new { amount = expected, paidOn = Day(1) });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            last = PartnerWorld.AsObject(await response.DataAsync());
        }

        Assert.True(last!["agreementSettled"]!.GetValue<bool>());
        Assert.Equal("Settled", factory.Scalar<string>("SELECT Status FROM veh.VehicleFinanceAgreements WHERE VehicleFinanceAgreementId = @a", ("@a", agreementId)));
        Assert.Equal(550_000m, factory.Scalar<decimal>("SELECT SUM(Amount) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));

        // The history stays on the schedule, but nothing more can be paid.
        var schedule = await Schedule(w, vehicle);
        Assert.Equal(4, schedule.Count);
        await PartnerWorld.AssertRefusedAsync(await Pay(w, vehicle, schedule[0], new { amount = 1, paidOn = Day(1) }), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task A_payment_is_checked_and_a_refused_one_changes_and_posts_nothing()
    {
        var (w, vehicle, _, _) = await LeasedAsync();
        var first = (await Schedule(w, vehicle))[0];
        async Task<List<(string Field, string Code, string Message)>> Errors(object body, string? rowVersion = null) => await PartnerWorld.ErrorsAsync(await Pay(w, vehicle, first, body, rowVersion: rowVersion));

        Assert.Contains(await Errors(new { }), e => e is { Field: "amount", Code: Msg.Required });
        Assert.Contains(await Errors(new { amount = 0 }), e => e is { Field: "amount", Code: Msg.Min });
        Assert.Contains(await Errors(new { amount = -5 }), e => e is { Field: "amount", Code: Msg.Min });
        Assert.Contains(await Errors(new { amount = 10, paidOn = Day(-2) }), e => e is { Field: "paidOn", Code: Msg.NotFuture });
        Assert.Contains(await Errors(new { amount = 10, paidOn = Day(61) }), e => e is { Field: "paidOn", Code: Msg.Min });   // acquired 60 days ago
        Assert.Contains(await Errors(new { amount = 10, paymentMode = "Barter" }), e => e is { Field: "paymentMode", Code: Msg.OneOf });
        Assert.Contains(await Errors(new { amount = 10, reference = new string('x', 61) }), e => e is { Field: "reference", Code: Msg.MaxLength });
        Assert.Contains(await Errors(new { amount = 10 }, rowVersion: ""), e => e is { Field: "rowVersion", Code: Msg.Required });

        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal(0m, (await Schedule(w, vehicle))[0]["paidAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Two_payments_entered_at_once_cannot_together_pay_more_than_is_due()
    {
        var (w, vehicle, _, _) = await LeasedAsync();
        var first = (await Schedule(w, vehicle))[0];

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Pay(w, vehicle, first, new { amount = 100_000, paidOn = Day(1) })));   // two of these are more than the 150,000 due
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        // A loser either held a stale copy (409) or read after the winner committed and asked for more than is left (400).
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, r.StatusCode.ToString()));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
        Assert.Equal(100_000m, (await Schedule(w, vehicle))[0]["paidAmount"]!.GetValue<decimal>());

        // A copy of the row read before the payment is stale, even for an amount that would fit.
        Assert.Equal(HttpStatusCode.Conflict, (await Pay(w, vehicle, first, new { amount = 1, paidOn = Day(1) })).StatusCode);
    }

    [Fact]
    public async Task An_installment_of_another_vehicle_is_not_found()
    {
        var (w, vehicle, _, _) = await LeasedAsync();
        var (_, other, _, _) = await LeasedAsync(w);
        var foreign = (await Schedule(w, other))[0];

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/installments/{foreign["id"]!.GetValue<int>()}/payments",
            new { amount = 1, rowVersion = foreign["rowVersion"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Amounts_are_hidden_from_and_payments_refused_to_those_without_finance_and_cost_permission()
    {
        var (w, vehicle, _, _) = await LeasedAsync();
        var first = (await Schedule(w, vehicle))[0];

        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        var seen = (await Schedule(w, vehicle, viewer))[0];
        foreach (var field in new[] { "expectedAmount", "paidAmount", "remainingAmount" }) Assert.False(seen.ContainsKey(field), field);
        Assert.Equal(1, seen["installmentNo"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await Pay(w, vehicle, first, new { amount = 1 }, viewer)).StatusCode);

        var financeOnly = w.As("Finance clerk", 7, PermissionCodes.VEH_VIEW, PermissionCodes.FIN_INSTALLMENT_PAY, PermissionCodes.VEH_FIELD_FINANCE_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await Pay(w, vehicle, first, new { amount = 1 }, financeOnly)).StatusCode);
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }
}
