using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Vehicles;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>
/// A tenant of its own with the people who work on vehicles, and the partners a vehicle points at. Vehicles cannot be activated through
/// the API until the finance stage delivers activation (S2-VH-09), so a test that needs one in the fleet moves a Draft to Active in the database.
/// </summary>
internal sealed class VehicleWorld
{
    private static int _counter = 100;

    public static readonly string[] VehiclePermissions =
    [
        PermissionCodes.VEH_VIEW, PermissionCodes.VEH_CREATE, PermissionCodes.VEH_EDIT, PermissionCodes.VEH_ACQUISITION_EDIT, PermissionCodes.VEH_ACTIVATE,
        PermissionCodes.VEH_CATEGORY_CHANGE, PermissionCodes.VEH_STATUS_CHANGE, PermissionCodes.VEH_DISPOSE, PermissionCodes.VEH_ITEM_MANAGE, PermissionCodes.VEH_DRIVER_ASSIGN,
        PermissionCodes.VEH_FIELD_COST_VIEW, PermissionCodes.VEH_FIELD_FINANCE_VIEW, PermissionCodes.VEH_FIELD_PROFIT_VIEW, PermissionCodes.VEH_EXPORT, PermissionCodes.FIN_INSTALLMENT_PAY, PermissionCodes.FIN_ADJUSTMENT_POST,
        PermissionCodes.FIN_RECURRING_MANAGE, PermissionCodes.FIN_RECURRING_AUTOPOST, PermissionCodes.FIN_DUE_CONFIRM, PermissionCodes.FIN_DUE_WAIVE, PermissionCodes.ADM_CONFIG_MANAGE,
        PermissionCodes.DOC_VIEW, PermissionCodes.DOC_UPLOAD, PermissionCodes.DOC_RENEW, PermissionCodes.DOC_REJECT, PermissionCodes.DOC_DOWNLOAD, PermissionCodes.DOC_DOWNLOAD_SENSITIVE, PermissionCodes.DOC_REGISTER_VIEW,
        PermissionCodes.ADM_MASTER_MANAGE, PermissionCodes.ADM_NOTIFICATION_MANAGE,
    ];

    public PartnerWorld Partners { get; }
    public ApiFactory Factory => Partners.Factory;
    public Guid Tenant => Partners.Tenant;
    /// <summary>Can do everything with vehicles and partners.</summary>
    public HttpClient Admin { get; }
    public int TruckType { get; private set; }
    public int PickupType { get; private set; }
    public int Make { get; private set; }
    public int ItemType { get; private set; }

    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;

    private VehicleWorld(PartnerWorld partners, WebApplicationFactory<Program> host)
    {
        Partners = partners;
        _host = host;
        Admin = As("Fleet Admin", 1, [.. VehiclePermissions, .. PartnerWorld.Everything]);
    }

    public static async Task<VehicleWorld> CreateAsync(ApiFactory factory, WebApplicationFactory<Program>? host = null)
    {
        var world = new VehicleWorld(await PartnerWorld.CreateAsync(factory, host), host ?? factory);
        async Task<int> Lookup(string type, string description) =>
            (await (await world.Admin.GetAsync($"/api/lookups/{type}")).DataAsync()).EnumerateArray().First(v => v.GetProperty("description").GetString() == description).GetProperty("id").GetInt32();
        world.TruckType = await Lookup("VEHICLE_TYPE", "Truck");
        world.PickupType = await Lookup("VEHICLE_TYPE", "Pickup");
        world.Make = await Lookup("MAKE", "Hino");
        world.ItemType = await Lookup("ATTACHED_ITEM_TYPE", "Container");
        return world;
    }

    public HttpClient As(string name, int userId, params string[] permissions) => Partners.As(name, userId, permissions);

    // ── Bodies ──────────────────────────────────────────────────────────────────────

    public static string NextReg() => $"LEA-{Interlocked.Increment(ref _counter)}";

    /// <summary>A truck with everything required: a load capacity, a fuel type, a make and a model.</summary>
    public JsonObject Truck(string? regNo = null) => new()
    {
        ["registrationNo"] = regNo ?? NextReg(),
        ["vehicleTypeId"] = TruckType,
        ["makeId"] = Make,
        ["model"] = "500",
        ["fuelType"] = "Diesel",
        ["loadCapacity"] = 20,
        ["capacityUnit"] = "Tonne"
    };

    public JsonObject Pickup(string? regNo = null) => new()
    {
        ["registrationNo"] = regNo ?? NextReg(), ["vehicleTypeId"] = PickupType, ["makeId"] = Make, ["model"] = "Hilux", ["fuelType"] = "Diesel"
    };

    // ── Calls ───────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> PostAsync(JsonObject body, HttpClient? client = null) => (client ?? Admin).PostAsJsonAsync("/api/vehicles", body);

    public async Task<JsonObject> CreateAsync(JsonObject body, HttpClient? client = null)
    {
        var response = await PostAsync(body, client);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    public async Task<JsonObject> GetAsync(int id, HttpClient? client = null)
    {
        var response = await (client ?? Admin).GetAsync($"/api/vehicles/{id}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    public async Task<HttpResponseMessage> PutAsync(JsonObject vehicle, HttpClient? client = null) =>
        await (client ?? Admin).PutAsJsonAsync($"/api/vehicles/{vehicle["id"]!.GetValue<int>()}", vehicle);

    public static int Id(JsonObject vehicle) => vehicle["id"]!.GetValue<int>();

    /// <summary>A vehicle in the fleet: created as a Draft, then made Active in the database (activation is S2-VH-09).</summary>
    public async Task<JsonObject> ActiveAsync(JsonObject? body = null, string? acquired = null)
    {
        var vehicle = await CreateAsync(body ?? Truck());
        Factory.Execute("UPDATE veh.Vehicles SET Status = 'Active', CurrentCategory = 'SelfOwned', AcquisitionDate = @a WHERE VehicleId = @i", ("@i", Id(vehicle)), ("@a", (object?)acquired ?? DBNull.Value));
        return await GetAsync(Id(vehicle));
    }

    public void SetStatus(int vehicleId, string status) => Factory.Execute("UPDATE veh.Vehicles SET Status = @s WHERE VehicleId = @i", ("@i", vehicleId), ("@s", status));

    /// <summary>A minimal file whose bytes sniff as a real PDF, for a document or receipt upload.</summary>
    public static byte[] Pdf(int length = 400)
    {
        var bytes = new byte[Math.Max(length, 12)];
        Random.Shared.NextBytes(bytes);
        "%PDF-1.4\n"u8.CopyTo(bytes);
        return bytes;
    }

    /// <summary>Uploads a Registration Book for the vehicle (BR-VH-015), so a real activation (through <c>POST .../activate</c>, unlike <see cref="ActiveAsync"/>) is not refused for the paperwork alone.</summary>
    public async Task UploadRegistrationBookAsync(int vehicleId, HttpClient? client = null)
    {
        var types = await (await Admin.GetAsync("/api/documents/types?appliesTo=Vehicle")).DataAsync();
        var typeId = types.EnumerateArray().First(t => t.GetProperty("code").GetString() == "REGISTRATION_BOOK").GetProperty("id").GetInt32();
        using var form = new System.Net.Http.MultipartFormDataContent();
        form.Add(new StringContent(typeId.ToString()), "documentTypeId");
        var part = new System.Net.Http.ByteArrayContent(Pdf());
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "reg-book.pdf");
        var response = await (client ?? Admin).PostAsync($"/api/vehicles/{vehicleId}/documents", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    // ── Partners of the tenant ──────────────────────────────────────────────────────

    public async Task<int> PartnerAsync(string role, JsonObject? extra = null)
    {
        var body = role == "Driver" ? Partners.Person(role: "Driver") : Partners.Company(roles: role);
        if (role == "Driver") body["driver"] = PartnerWorld.Driver();
        else if (role != "Vendor") body.Remove("vendor");
        if (role is "Customer") body["customer"] = new JsonObject { ["customerType"] = "Factory", ["billingCycle"] = "Monthly" };
        if (role is "Bank" or "Customer") body["email"] = "ops@example.pk";
        if (extra is not null) foreach (var (key, value) in extra) body[key] = value?.DeepClone();
        return (await Partners.CreateAsync(body))["id"]!.GetValue<int>();
    }

    public async Task<int> DriverAsync() => await PartnerAsync("Driver");
}

/// <summary>Stands in for the finance module: says a vehicle has a balance outstanding, so a category change must be refused (BR-VH-007).</summary>
public sealed class FakeFinanceGuard : IVehicleFinanceGuard
{
    public static volatile bool Outstanding;

    public Task<bool> HasOutstandingFinanceAsync(int vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(Outstanding);

    public static WebApplicationFactory<Program> Host(ApiFactory factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IVehicleFinanceGuard, FakeFinanceGuard>()));
}
