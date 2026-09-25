namespace VMS.Modules.Trips.Services;

/// <summary>
/// One item of the customer activation checklist (FSD §10: "at least one active default billing address, at
/// least one active contact, a billing configuration record, and at least one active invoice template"). None of
/// those four tables exist yet in CC-03 — Contacts/Addresses (CC-04), Billing Configuration (CC-05) and Invoice
/// Templates (CC-07) each add their own table *after* Customer, not before, so CC-03 cannot query them.
/// <para>
/// Each of those later tasks registers its own <see cref="ICustomerActivationRequirement"/> (<c>services.AddScoped
/// &lt;ICustomerActivationRequirement, ...&gt;()</c> — .NET DI resolves every registration of the same interface into
/// one <see cref="IEnumerable{T}"/>, so no central list needs editing) the same session it adds the table the
/// check reads. Until then <see cref="Services.CustomerService"/> sees an empty collection and every customer
/// passes the checklist trivially — honest, not faked: there is nothing to fail yet.
/// </para>
/// </summary>
internal interface ICustomerActivationRequirement
{
    /// <summary>Null if satisfied; otherwise the missing-item text shown on the activation checklist.</summary>
    Task<string?> CheckAsync(int customerId, CancellationToken ct = default);
}
