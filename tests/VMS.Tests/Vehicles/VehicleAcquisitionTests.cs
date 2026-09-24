using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S3-FIN-04: the acquisition block of a Draft vehicle (FSD §18.1).</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleAcquisitionTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private static async Task<JsonObject> BlockAsync(VehicleWorld w, JsonObject vehicle, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    private static async Task<HttpResponseMessage> SaveAsync(VehicleWorld w, JsonObject vehicle, object fields, HttpClient? client = null)
    {
        var block = await BlockAsync(w, vehicle);
        var body = PartnerWorld.AsObject(System.Text.Json.JsonSerializer.SerializeToElement(fields));
        body["rowVersion"] = block["rowVersion"]!.GetValue<string>();
        return await (client ?? w.Admin).PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", body);
    }

    private static object Purchase(int? seller = null, decimal price = 5_000_000, decimal paid = 2_000_000) => new
    {
        acquisitionDate = Day(5), acquisitionType = "Purchase", sellerId = seller, purchasePrice = price, amountPaid = paid,
        paymentMode = "BankTransfer", paymentReference = "TRX-1", registrationCost = 45_000
    };

    [Fact]
    public async Task The_block_is_saved_on_a_draft_and_read_back_and_writes_nothing_to_the_ledger()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var seller = await w.PartnerAsync("Vendor");

        var saved = await SaveAsync(w, vehicle, Purchase(seller));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var block = await BlockAsync(w, vehicle);
        Assert.Equal(5_000_000m, block["purchasePrice"]!.GetValue<decimal>());
        Assert.Equal(2_000_000m, block["amountPaid"]!.GetValue<decimal>());
        Assert.Equal("BankTransfer", block["paymentMode"]!.GetValue<string>());
        Assert.Equal(45_000m, block["registrationCost"]!.GetValue<decimal>());
        Assert.Equal(seller, block["seller"]!["id"]!.GetValue<int>());
        Assert.Equal(Day(5), block["acquisitionDate"]!.GetValue<string>());
        Assert.Equal("Purchase", (await w.GetAsync(Id(vehicle)))["acquisitionType"]!.GetValue<string>());

        // FR-VH-012: a Draft posts nothing.
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task Saving_again_replaces_the_block_and_clears_a_payment_that_is_no_longer_there()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        Assert.True((await SaveAsync(w, vehicle, Purchase())).IsSuccessStatusCode);

        var again = await SaveAsync(w, vehicle, new { acquisitionDate = Day(4), acquisitionType = "Purchase", purchasePrice = 4_800_000, amountPaid = 0 });
        Assert.True(again.IsSuccessStatusCode, await again.Content.ReadAsStringAsync());

        var block = await BlockAsync(w, vehicle);
        Assert.Equal(4_800_000m, block["purchasePrice"]!.GetValue<decimal>());
        Assert.Equal(0m, block["amountPaid"]!.GetValue<decimal>());
        Assert.Null(block["paymentMode"]);
        Assert.Null(block["paymentReference"]);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleAcquisitions WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task Each_field_is_checked_and_what_was_paid_can_never_exceed_the_price()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        async Task<List<(string Field, string Code, string Message)>> Errors(object body) => await PartnerWorld.ErrorsAsync(await SaveAsync(w, vehicle, body));

        var none = await Errors(new { });
        Assert.Contains(none, e => e is { Field: "acquisitionDate", Code: Msg.Required });
        Assert.Contains(none, e => e is { Field: "acquisitionType", Code: Msg.Required });

        var future = await Errors(new { acquisitionDate = Day(-3), acquisitionType = "Purchase" });
        Assert.Contains(future, e => e is { Field: "acquisitionDate", Code: Msg.VhAcquisitionDateFuture });

        var over = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Purchase", purchasePrice = 1000, amountPaid = 1000.01, paymentMode = "Cash" });
        Assert.Contains(over, e => e is { Field: "amountPaid", Code: Msg.VhPaidExceedsPrice });   // BR-VH-021

        var noPaid = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Purchase", purchasePrice = 1000 });
        Assert.Contains(noPaid, e => e is { Field: "amountPaid", Code: Msg.Required });

        var noMode = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Purchase", purchasePrice = 1000, amountPaid = 10 });
        Assert.Contains(noMode, e => e is { Field: "paymentMode", Code: Msg.Required });

        var odd = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Gift", purchasePrice = 0, registrationCost = -1, amountPaid = 0, paymentReference = new string('x', 61) });
        Assert.Contains(odd, e => e is { Field: "acquisitionType", Code: Msg.OneOf });
        Assert.Contains(odd, e => e is { Field: "purchasePrice", Code: Msg.Invalid });
        Assert.Contains(odd, e => e is { Field: "registrationCost", Code: Msg.Invalid });

        var wrongMode = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Purchase", purchasePrice = 1000, amountPaid = 10, paymentMode = "Barter" });
        Assert.Contains(wrongMode, e => e is { Field: "paymentMode", Code: Msg.OneOf });

        var noPrice = await Errors(new { acquisitionDate = Day(2), acquisitionType = "Purchase", amountPaid = 10, paymentMode = "Cash" });
        Assert.Contains(noPrice, e => e.Field == "amountPaid");

        // Nothing invalid was kept.
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleAcquisitions WHERE VehicleId = @i", ("@i", Id(vehicle))));
    }

    [Fact]
    public async Task The_seller_must_be_a_partner_of_the_company_that_can_still_be_chosen()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var missing = await PartnerWorld.ErrorsAsync(await SaveAsync(w, vehicle, Purchase(seller: 999_999)));
        Assert.Contains(missing, e => e is { Field: "sellerId", Code: Msg.Invalid });

        // Any role will do: the seller is a source, not a counterparty.
        var driver = await w.DriverAsync();
        Assert.True((await SaveAsync(w, vehicle, Purchase(driver))).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Only_a_draft_takes_an_acquisition_because_the_postings_are_written_at_activation()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        await PartnerWorld.AssertRefusedAsync(await SaveAsync(w, vehicle, Purchase()), "status", Msg.VhWrongStatus);
    }

    [Fact]
    public async Task A_change_made_by_someone_else_in_the_meantime_is_refused_not_overwritten()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var stale = await BlockAsync(w, vehicle);

        var edit = await w.GetAsync(Id(vehicle));
        edit["remarks"] = "Changed by someone";
        Assert.True((await w.PutAsync(edit)).IsSuccessStatusCode);

        var body = PartnerWorld.AsObject(System.Text.Json.JsonSerializer.SerializeToElement(Purchase()));
        body["rowVersion"] = stale["rowVersion"]!.GetValue<string>();
        var response = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", body);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_amounts_are_hidden_from_and_cannot_be_entered_by_someone_without_cost_permission()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        Assert.True((await SaveAsync(w, vehicle, Purchase())).IsSuccessStatusCode);

        var fleetManager = w.As("Fleet Manager", 5, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ACQUISITION_EDIT);
        var seen = await BlockAsync(w, vehicle, fleetManager);
        Assert.False(seen.ContainsKey("purchasePrice"));
        Assert.False(seen.ContainsKey("amountPaid"));
        Assert.False(seen.ContainsKey("registrationCost"));
        Assert.Equal("Purchase", seen["acquisitionType"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(w, vehicle, Purchase(), fleetManager)).StatusCode);

        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(w, vehicle, Purchase(), viewer)).StatusCode);
    }

    [Fact]
    public async Task Saving_the_block_is_recorded_under_the_vehicle_and_the_price_is_marked_as_restricted()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        Assert.True((await SaveAsync(w, vehicle, Purchase())).IsSuccessStatusCode);

        var permission = factory.Scalar<string?>(
            "SELECT TOP 1 RequiredPermission FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'VehicleAcquisition' AND Field = 'PurchasePrice' AND RootEntity = 'Vehicle' AND RootRecordId = @i",
            ("@t", w.Tenant), ("@i", Id(vehicle).ToString()));
        Assert.Equal(PermissionCodes.VEH_FIELD_COST_VIEW, permission);
    }
}
