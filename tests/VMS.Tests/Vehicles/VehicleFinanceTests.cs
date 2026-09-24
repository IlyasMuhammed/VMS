using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S3-FIN-02, 03, 05: the finance block of a Draft vehicle and the tables behind it (FSD §18.2).</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleFinanceTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    /// <summary>A Draft with its acquisition saved: price 5,000,000 of which 2,000,000 was paid, acquired five days ago.</summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle, int Bank, int FinanceType)> DraftAsync(decimal price = 5_000_000, decimal paid = 2_000_000)
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition")).DataAsync());
        var saved = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", new
        {
            acquisitionDate = Day(5), acquisitionType = "Lease", purchasePrice = price, amountPaid = paid, paymentMode = "BankTransfer", rowVersion = block["rowVersion"]!.GetValue<string>()
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var bank = await w.PartnerAsync("Bank");
        var types = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().ToList();
        return (w, vehicle, bank, types.First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32());
    }

    private static JsonObject Terms(int financeType, int bank, string? agreementNo = null, decimal financeAmount = 3_000_000, decimal downPayment = 2_000_000) => new()
    {
        ["financeTypeId"] = financeType, ["bankId"] = bank, ["agreementNo"] = agreementNo ?? $"AGR-{Guid.NewGuid():N}"[..14],
        ["agreementDate"] = Day(4), ["financeAmount"] = financeAmount, ["downPayment"] = downPayment, ["installmentAmount"] = 150_000, ["frequency"] = "Monthly",
        ["tenure"] = 24, ["firstDueDate"] = Day(-26), ["markupRate"] = 14.5m, ["residualAmount"] = 100_000, ["securityDeposit"] = 50_000
    };

    private static Task<HttpResponseMessage> Put(VehicleWorld w, JsonObject vehicle, JsonObject body, HttpClient? client = null) =>
        (client ?? w.Admin).PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", body);

    private static async Task<JsonObject?> Current(VehicleWorld w, JsonObject vehicle, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/finance");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var data = await response.DataAsync();
        return data.ValueKind == System.Text.Json.JsonValueKind.Null ? null : PartnerWorld.AsObject(data);
    }

    [Fact]
    public async Task A_vehicle_starts_with_no_agreement_and_the_terms_are_saved_and_read_back_with_the_total_payable()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.Null(await Current(w, vehicle));

        var saved = await Put(w, vehicle, Terms(type, bank, "AGR-77"));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var agreement = (await Current(w, vehicle))!;
        Assert.Equal("Draft", agreement["status"]!.GetValue<string>());
        Assert.Equal("Bank Lease", agreement["financeType"]!.GetValue<string>());
        Assert.Equal(bank, agreement["bank"]!["id"]!.GetValue<int>());
        Assert.Equal("AGR-77", agreement["agreementNo"]!.GetValue<string>());
        Assert.Equal(3_000_000m, agreement["financeAmount"]!.GetValue<decimal>());
        Assert.Equal(24, agreement["tenure"]!.GetValue<int>());
        Assert.Equal(3_600_000m, agreement["totalPayable"]!.GetValue<decimal>());   // 150,000 x 24
        Assert.Equal(14.5m, agreement["markupRate"]!.GetValue<decimal>());

        // Nothing is scheduled or posted until the vehicle is activated.
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleInstallments WHERE VehicleId = @i", ("@i", Id(vehicle))));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task Saving_again_changes_the_one_agreement_and_a_stale_copy_is_refused()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank, "AGR-1"))).IsSuccessStatusCode);
        var first = (await Current(w, vehicle))!;

        var edit = Terms(type, bank, "AGR-1");
        edit["installmentAmount"] = 160_000;
        edit["rowVersion"] = first["rowVersion"]!.GetValue<string>();
        Assert.True((await Put(w, vehicle, edit)).IsSuccessStatusCode);
        Assert.Equal(160_000m, (await Current(w, vehicle))!["installmentAmount"]!.GetValue<decimal>());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleFinanceAgreements WHERE VehicleId = @i", ("@i", Id(vehicle))));

        // The row version of the first read is out of date now.
        Assert.Equal(HttpStatusCode.Conflict, (await Put(w, vehicle, edit)).StatusCode);

        // A change to a saved agreement must say which version it is changing.
        var blind = Terms(type, bank, "AGR-1");
        await PartnerWorld.AssertRefusedAsync(await Put(w, vehicle, blind), "rowVersion", Msg.Required);
    }

    [Fact]
    public async Task Each_term_is_checked_and_the_agreement_date_may_follow_the_acquisition_by_ninety_days_at_most()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        async Task<List<(string Field, string Code, string Message)>> Errors(JsonObject body) => await PartnerWorld.ErrorsAsync(await Put(w, vehicle, body));

        var empty = await Errors(new JsonObject());
        foreach (var field in new[] { "financeTypeId", "bankId", "agreementNo", "agreementDate", "financeAmount", "downPayment", "frequency", "firstDueDate" })
            Assert.Contains(empty, e => e.Field == field);
        Assert.Contains(empty, e => e is { Field: "installmentAmount", Code: Msg.VhInstallmentRequired });
        Assert.Contains(empty, e => e is { Field: "tenure", Code: Msg.VhInstallmentRequired });

        var late = Terms(type, bank); late["agreementDate"] = Day(-86);   // acquired 5 days ago: 95 days after
        Assert.Contains(await Errors(late), e => e is { Field: "agreementDate", Code: Msg.Max });
        var onTheLimit = Terms(type, bank); onTheLimit["agreementDate"] = Day(-85); onTheLimit["firstDueDate"] = Day(-85);
        onTheLimit["tenure"] = 0;   // wrong on purpose, so this stays a check and does not save
        var limitErrors = await Errors(onTheLimit);
        Assert.DoesNotContain(limitErrors, e => e.Field == "agreementDate");
        Assert.Contains(limitErrors, e => e.Field == "tenure");

        var early = Terms(type, bank); early["firstDueDate"] = Day(10);
        Assert.Contains(await Errors(early), e => e is { Field: "firstDueDate", Code: Msg.Min });

        var tenureLow = Terms(type, bank); tenureLow["tenure"] = 0;
        Assert.Contains(await Errors(tenureLow), e => e is { Field: "tenure", Code: Msg.Min });
        var tenureHigh = Terms(type, bank); tenureHigh["tenure"] = 121;
        Assert.Contains(await Errors(tenureHigh), e => e is { Field: "tenure", Code: Msg.Max });

        var odd = Terms(type, bank); odd["frequency"] = "Weekly"; odd["installmentAmount"] = 0; odd["financeAmount"] = 0; odd["markupRate"] = 101; odd["residualAmount"] = -1; odd["securityDeposit"] = -1;
        var errors = await Errors(odd);
        Assert.Contains(errors, e => e is { Field: "frequency", Code: Msg.OneOf });
        foreach (var field in new[] { "installmentAmount", "financeAmount", "markupRate", "residualAmount", "securityDeposit" })
            Assert.Contains(errors, e => e.Field == field);

        Assert.Null(await Current(w, vehicle));
    }

    [Fact]
    public async Task The_acquisition_date_must_be_saved_first()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var bank = await w.PartnerAsync("Bank");
        var lease = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();

        var errors = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(lease, bank)));
        Assert.Contains(errors, e => e is { Field: "acquisitionDate", Code: Msg.Required });
    }

    [Fact]
    public async Task The_bank_must_hold_the_bank_role_and_a_fully_paid_vehicle_has_no_agreement()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        var vendor = await w.PartnerAsync("Vendor");

        var wrongRole = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(type, vendor)));
        Assert.Contains(wrongRole, e => e is { Field: "bankId", Code: Msg.VhCounterpartyLacksRole });

        var missing = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(type, 999_999)));
        Assert.Contains(missing, e => e is { Field: "bankId", Code: Msg.Invalid });

        var fullyPaid = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Fully Paid").GetProperty("id").GetInt32();
        var noAgreement = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(fullyPaid, bank)));
        Assert.Contains(noAgreement, e => e is { Field: "financeTypeId", Code: Msg.Invalid });

        var noType = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(999_999, bank)));
        Assert.Contains(noType, e => e is { Field: "financeTypeId", Code: Msg.Invalid });
    }

    [Fact]
    public async Task An_agreement_number_belongs_to_one_bank_and_one_vehicle()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank, "AGR-SHARED"))).IsSuccessStatusCode);

        var other = await w.CreateAsync(w.Truck());
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(other)}/acquisition")).DataAsync());
        Assert.True((await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(other)}/acquisition", new
        {
            acquisitionDate = Day(5), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "Cash", rowVersion = block["rowVersion"]!.GetValue<string>()
        })).IsSuccessStatusCode);

        var duplicate = await PartnerWorld.ErrorsAsync(await Put(w, other, Terms(type, bank, "AGR-SHARED")));
        Assert.Contains(duplicate, e => e is { Field: "agreementNo", Code: Msg.VhChassisOrEngineDuplicate } && e.Message.Contains(Id(vehicle) > 0 ? "VH-" : ""));

        // The same number at another bank is another agreement.
        var otherBank = await w.PartnerAsync("Bank");
        Assert.True((await Put(w, other, Terms(type, otherBank, "AGR-SHARED"))).IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_down_payment_must_equal_the_amount_paid_at_creation_and_a_mismatch_blocks_the_save()
    {
        var (w, vehicle, bank, type) = await DraftAsync(price: 5_000_000, paid: 2_000_000);

        var errors = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, Terms(type, bank, financeAmount: 3_000_000, downPayment: 1_500_000)));
        Assert.Contains(errors, e => e is { Field: "downPayment", Code: Msg.VhDownPaymentMismatch } && e.Message.Contains("1,500,000") && e.Message.Contains("2,000,000"));   // BR-VH-009
        Assert.Null(await Current(w, vehicle));
    }

    [Fact]
    public async Task Finance_and_down_payment_not_adding_up_to_the_price_asks_and_goes_ahead_when_confirmed()
    {
        var (w, vehicle, bank, type) = await DraftAsync(price: 5_000_000, paid: 2_000_000);

        var unreconciled = Terms(type, bank, financeAmount: 2_800_000, downPayment: 2_000_000);   // 4,800,000 against 5,000,000
        var asked = await PartnerWorld.ErrorsAsync(await Put(w, vehicle, unreconciled));
        Assert.Contains(asked, e => e is { Field: "financeAmount", Code: Msg.VhFinanceNotReconciled } && e.Message.Contains("4,800,000") && e.Message.Contains("5,000,000"));   // BR-VH-010
        Assert.Null(await Current(w, vehicle));

        unreconciled["confirmMismatch"] = true;
        Assert.True((await Put(w, vehicle, unreconciled)).IsSuccessStatusCode);
        Assert.Equal(2_800_000m, (await Current(w, vehicle))!["financeAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task A_draft_agreement_can_be_removed_and_only_a_draft_takes_finance()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank))).IsSuccessStatusCode);

        Assert.True((await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/finance")).IsSuccessStatusCode);
        Assert.Null(await Current(w, vehicle));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/finance")).StatusCode);

        w.SetStatus(Id(vehicle), "Active");
        await PartnerWorld.AssertRefusedAsync(await Put(w, vehicle, Terms(type, bank)), "status", Msg.VhWrongStatus);
        await PartnerWorld.AssertRefusedAsync(await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/finance"), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task A_vehicle_can_have_only_one_open_agreement_though_settled_ones_stay_as_history()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank, "AGR-OLD"))).IsSuccessStatusCode);

        // BR-VH-004: the database refuses a second open one, whatever the code does.
        var second = () => factory.Execute(
            "INSERT INTO veh.VehicleFinanceAgreements (TenantId, VehicleId, FinanceTypeId, BankId, AgreementNo, AgreementDate, FinanceAmount, DownPayment, InstallmentAmount, Frequency, Tenure, FirstDueDate, Status, CreatedBy, CreatedOn, ModifiedOn) " +
            "VALUES (@t, @v, @ft, @b, 'AGR-NEW', SYSDATETIME(), 1, 1, 1, 'Monthly', 1, SYSDATETIME(), 'Active', 1, SYSDATETIME(), SYSDATETIME())",
            ("@t", w.Tenant), ("@v", Id(vehicle)), ("@ft", type), ("@b", bank));
        Assert.ThrowsAny<Exception>(second);

        factory.Execute("UPDATE veh.VehicleFinanceAgreements SET Status = 'Settled' WHERE VehicleId = @v", ("@v", Id(vehicle)));
        second();   // the old one is history now, so a new one may be open
        Assert.Equal(2, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleFinanceAgreements WHERE VehicleId = @v", ("@v", Id(vehicle))));
    }

    [Fact]
    public async Task Finance_amounts_are_hidden_from_and_cannot_be_entered_by_someone_without_finance_permission()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank))).IsSuccessStatusCode);

        var fleetManager = w.As("Fleet Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ACQUISITION_EDIT);
        var seen = (await Current(w, vehicle, fleetManager))!;
        foreach (var field in new[] { "financeAmount", "downPayment", "installmentAmount", "residualAmount", "securityDeposit", "totalPayable" })
            Assert.False(seen.ContainsKey(field), field);
        Assert.Equal(24, seen["tenure"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.Forbidden, (await Put(w, vehicle, Terms(type, bank), fleetManager)).StatusCode);
        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(w, vehicle, Terms(type, bank), viewer)).StatusCode);
    }

    [Fact]
    public async Task Finance_amounts_in_the_history_are_marked_as_restricted()
    {
        var (w, vehicle, bank, type) = await DraftAsync();
        Assert.True((await Put(w, vehicle, Terms(type, bank))).IsSuccessStatusCode);

        var permission = factory.Scalar<string?>(
            "SELECT TOP 1 RequiredPermission FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'VehicleFinanceAgreement' AND Field = 'FinanceAmount' AND RootEntity = 'Vehicle' AND RootRecordId = @i",
            ("@t", w.Tenant), ("@i", Id(vehicle).ToString()));
        Assert.Equal(PermissionCodes.VEH_FIELD_FINANCE_VIEW, permission);
    }
}
