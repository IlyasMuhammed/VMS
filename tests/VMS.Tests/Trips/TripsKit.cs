using Microsoft.AspNetCore.Mvc.Testing;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;

namespace VMS.Tests.Trips;

/// <summary>A tenant of its own for the Trip/Billing/Invoicing/Customer Ledger module's own tests, separate from
/// [[PartnerWorld]]/[[VehicleWorld]] since this module (CC-00's decision) does not depend on Partners/Vehicles
/// existing to exercise its own currency/customer/trip/invoice/ledger endpoints.</summary>
internal sealed class TripsWorld
{
    public static readonly string[] Everything =
    [
        PermissionCodes.TRP_CURRENCY_MANAGE, PermissionCodes.TRP_EXCHANGERATE_MANAGE,
        PermissionCodes.TRP_CUSTOMER_VIEW, PermissionCodes.TRP_CUSTOMER_EDIT, PermissionCodes.TRP_TAXRULE_EDIT, PermissionCodes.TRP_TEMPLATE_EDIT,
        PermissionCodes.TRP_CITY_EDIT, PermissionCodes.TRP_ROUTE_EDIT,
        PermissionCodes.TRP_TRIPCONFIG_VIEW, PermissionCodes.TRP_TRIPCONFIG_EDIT,
        PermissionCodes.TRP_RATE_VIEW, PermissionCodes.TRP_RATE_CONFIGURE,
        PermissionCodes.TRP_TRIP_VIEW, PermissionCodes.TRP_TRIP_CREATE, PermissionCodes.TRP_TRIP_STATUS,
        PermissionCodes.TRP_TRIP_INACTIVATE, PermissionCodes.TRP_TRIP_REOPEN, PermissionCodes.TRP_TRIP_DOCUMENTS, PermissionCodes.TRP_POD_APPROVE,
        PermissionCodes.TRP_FUELCARD_EDIT,
        // Deliberately NOT PermissionCodes.TRP_TRIP_SKIPSTATUS — it must stay rare enough that "Everything" (this
        // module's stand-in for a back-office admin) still gets refused an invalid/skipped transition by default;
        // tests that need it grant it to a separate persona.
    ];

    private readonly WebApplicationFactory<Program> _host;
    public ApiFactory Factory { get; }
    public Guid Tenant { get; }
    /// <summary>Someone who can do everything this module defines so far.</summary>
    public HttpClient Admin { get; }

    private TripsWorld(ApiFactory factory, WebApplicationFactory<Program>? host)
    {
        Factory = factory;
        _host = host ?? factory;
        Tenant = factory.CreateTenant();
        Admin = As("Trips Admin", 1, Everything);
    }

    public static Task<TripsWorld> CreateAsync(ApiFactory factory, WebApplicationFactory<Program>? host = null) =>
        Task.FromResult(new TripsWorld(factory, host));

    public HttpClient As(string name, int userId, params string[] permissions) =>
        _host.CreateClient().WithToken(TestTokens.For(Tenant, name, userId, permissions));
}
