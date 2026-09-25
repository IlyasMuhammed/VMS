using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Services;

/// <summary>§10's activation checklist, item 1: "at least one active contact." Registered by CC-04, the task that
/// adds <see cref="CustomerContact"/> — see <see cref="ICustomerActivationRequirement"/> for why this isn't in CC-03.</summary>
internal sealed class RequiresActiveContactRequirement(TripsDbContext db, ITenantContext tenant) : ICustomerActivationRequirement
{
    public async Task<string?> CheckAsync(int customerId, CancellationToken ct = default) =>
        await db.CustomerContacts.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId && c.Status == ActiveInactiveStatuses.Active, ct)
            ? null : "At least one active contact is required.";
}

/// <summary>§10's activation checklist, item 2: "at least one active default billing address."</summary>
internal sealed class RequiresDefaultBillingAddressRequirement(TripsDbContext db, ITenantContext tenant) : ICustomerActivationRequirement
{
    public async Task<string?> CheckAsync(int customerId, CancellationToken ct = default) =>
        await db.CustomerBillingAddresses.AnyAsync(a => a.TenantId == tenant.TenantId && a.CustomerId == customerId
            && a.Status == ActiveInactiveStatuses.Active && a.IsDefault, ct)
            ? null : "An active default billing address is required.";
}

/// <summary>§10's activation checklist, item 3: "a billing configuration record." Checks for existence only — it
/// does not create one (that would make an activation *check* a hidden side effect); in practice a customer's
/// Billing Configuration tab already creates the default the first time anyone opens it (§13's own lazy default).</summary>
internal sealed class RequiresBillingConfigurationRequirement(TripsDbContext db, ITenantContext tenant) : ICustomerActivationRequirement
{
    public async Task<string?> CheckAsync(int customerId, CancellationToken ct = default) =>
        await db.CustomerBillingConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct)
            ? null : "A billing configuration record is required.";
}

/// <summary>§10's activation checklist, item 4 (the last one — CC-07): "at least one active invoice template."</summary>
internal sealed class RequiresActiveInvoiceTemplateRequirement(TripsDbContext db, ITenantContext tenant) : ICustomerActivationRequirement
{
    public async Task<string?> CheckAsync(int customerId, CancellationToken ct = default) =>
        await db.CustomerInvoiceTemplates.AnyAsync(t => t.TenantId == tenant.TenantId && t.CustomerId == customerId && t.Status == InvoiceTemplateStatuses.Active, ct)
            ? null : "At least one active invoice template is required.";
}
