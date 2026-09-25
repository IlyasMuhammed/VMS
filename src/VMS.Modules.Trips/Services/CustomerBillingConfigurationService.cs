using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

public interface ICustomerBillingConfigurationService
{
    /// <summary>The configuration in force right now, creating the default one (§13's own defaults) the first time
    /// a customer is asked for one — the same "nothing to configure until someone opens the screen" idiom every
    /// other lazily-seeded list in this repo uses, just scoped to one customer instead of one tenant.</summary>
    Task<CustomerBillingConfigurationModel> GetCurrentAsync(int customerId, CancellationToken ct = default);

    /// <summary>The configuration that was, or will be, in force on a given date (for an invoice dated in the past
    /// or the future) — §13: "the configuration values in force at invoice generation are snapshotted."</summary>
    Task<CustomerBillingConfigurationModel?> GetAsOfAsync(int customerId, DateOnly asOf, CancellationToken ct = default);

    Task<CustomerBillingConfigurationModel> SaveNewVersionAsync(int customerId, SaveCustomerBillingConfigurationRequest request, CancellationToken ct = default);
}

internal sealed class CustomerBillingConfigurationService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, ICustomerLookup customerLookup)
    : ICustomerBillingConfigurationService
{
    public async Task<CustomerBillingConfigurationModel> GetCurrentAsync(int customerId, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var current = await Current(customerId, ct);
        if (current is not null) return ToModel(current);

        var settings = await db.CurrencySettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId, ct);
        var defaults = new CustomerBillingConfiguration
        {
            CustomerId = customerId, CurrencyCode = settings?.BaseCurrencyCode ?? "PKR",
            EffectiveFrom = await clock.TodayAsync(tenant.TenantId)
        };
        db.CustomerBillingConfigurations.Add(defaults);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two requests raced to create the first row for the same customer; whichever lost simply reads what
            // the winner created (low-risk here — scoped to one customer being worked on, unlike a tenant-wide list).
            db.Entry(defaults).State = EntityState.Detached;
            var winner = await Current(customerId, ct);
            if (winner is null) throw;
            return ToModel(winner);
        }
        return ToModel(defaults);
    }

    public async Task<CustomerBillingConfigurationModel?> GetAsOfAsync(int customerId, DateOnly asOf, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var row = await db.CustomerBillingConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId && c.EffectiveFrom <= asOf && (c.EffectiveTo == null || c.EffectiveTo >= asOf))
            .OrderByDescending(c => c.EffectiveFrom).FirstOrDefaultAsync(ct);
        return row is null ? null : ToModel(row);
    }

    public async Task<CustomerBillingConfigurationModel> SaveNewVersionAsync(int customerId, SaveCustomerBillingConfigurationRequest request, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var current = await Current(customerId, ct);
        var today = await clock.TodayAsync(tenant.TenantId);
        var effectiveFrom = request.EffectiveFrom ?? today;

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (current is not null && effectiveFrom <= current.EffectiveFrom)
            Add("effectiveFrom", Msg.Min, ("Field", "Effective from"), ("Min", current.EffectiveFrom.AddDays(1).ToString("yyyy-MM-dd")));

        var settings = await db.CurrencySettings.AsNoTracking().FirstAsync(s => s.TenantId == tenant.TenantId, ct);
        var currencyCode = settings.MultiCurrencyEnabled && !string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? request.CurrencyCode.Trim().ToUpperInvariant() : settings.BaseCurrencyCode;
        if (currencyCode != settings.BaseCurrencyCode && !await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == currencyCode, ct))
            Add("currencyCode", Msg.TrpExchangeRateCurrencyUnknown, ("Code", currencyCode));

        var paymentTermsDays = request.PaymentTermsDays ?? 30;
        if (paymentTermsDays is < 0 or > 365) Add("paymentTermsDays", Msg.Max, ("Field", "Payment terms (days)"), ("Max", "365"));

        var evidencePageSize = request.EvidencePageSize ?? 50;
        if (evidencePageSize is < 10 or > 200) Add("evidencePageSize", Msg.OneOf, ("Field", "Evidence page size"), ("Allowed", "10-200"));

        var behaviour = string.IsNullOrWhiteSpace(request.DuplicateReferenceBehaviour) ? DuplicateReferenceBehaviours.Warn : request.DuplicateReferenceBehaviour;
        if (!DuplicateReferenceBehaviours.All.Contains(behaviour))
            Add("duplicateReferenceBehaviour", Msg.OneOf, ("Field", "Duplicate reference behaviour"), ("Allowed", string.Join(", ", DuplicateReferenceBehaviours.All)));

        var prefix = string.IsNullOrWhiteSpace(request.InvoiceNumberPrefix) ? "INV" : request.InvoiceNumberPrefix.Trim();
        if (prefix.Length > 10) Add("invoiceNumberPrefix", Msg.MaxLength, ("Field", "Invoice number prefix"), ("Max", "10"));

        if (request.DefaultBillingAddressId is { } addressId
            && !await db.CustomerBillingAddresses.AnyAsync(a => a.TenantId == tenant.TenantId && a.CustomerId == customerId && a.CustomerBillingAddressId == addressId, ct))
            Add("defaultBillingAddressId", Msg.Invalid, ("Field", "Default billing address"));

        if (request.StatementEmailContactId is { } contactId
            && !await db.CustomerContacts.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId && c.CustomerContactId == contactId, ct))
            Add("statementEmailContactId", Msg.Invalid, ("Field", "Statement email contact"));

        if (errors.Count > 0) throw new ValidationException(errors);

        var next = new CustomerBillingConfiguration
        {
            CustomerId = customerId, PaymentTermsDays = paymentTermsDays, CurrencyCode = currencyCode,
            DefaultBillingAddressId = request.DefaultBillingAddressId, DefaultInvoiceTemplateId = request.DefaultInvoiceTemplateId,
            InvoiceNumberPrefix = prefix, PodRequired = request.PodRequired, EvidenceRequired = request.EvidenceRequired,
            EvidencePageSize = evidencePageSize, DuplicateReferenceBehaviour = behaviour, CustomerReferenceRequired = request.CustomerReferenceRequired,
            StatementEmailContactId = request.StatementEmailContactId, EffectiveFrom = effectiveFrom
        };

        await db.InTransactionAsync(async ct2 =>
        {
            if (current is not null)
            {
                current.EffectiveTo = effectiveFrom.AddDays(-1);
                db.CustomerBillingConfigurations.Update(current);
            }
            db.CustomerBillingConfigurations.Add(next);
            await db.SaveChangesAsync(ct2);
        }, ct);

        return ToModel(next);
    }

    private Task<CustomerBillingConfiguration?> Current(int customerId, CancellationToken ct) =>
        db.CustomerBillingConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId && c.EffectiveTo == null, ct);

    private static CustomerBillingConfigurationModel ToModel(CustomerBillingConfiguration c) => new()
    {
        CustomerBillingConfigurationId = c.CustomerBillingConfigurationId, CustomerId = c.CustomerId,
        PaymentTermsDays = c.PaymentTermsDays, CurrencyCode = c.CurrencyCode, DefaultBillingAddressId = c.DefaultBillingAddressId,
        DefaultInvoiceTemplateId = c.DefaultInvoiceTemplateId, InvoiceNumberPrefix = c.InvoiceNumberPrefix, PodRequired = c.PodRequired,
        EvidenceRequired = c.EvidenceRequired, EvidencePageSize = c.EvidencePageSize, DuplicateReferenceBehaviour = c.DuplicateReferenceBehaviour,
        CustomerReferenceRequired = c.CustomerReferenceRequired, StatementEmailContactId = c.StatementEmailContactId,
        EffectiveFrom = c.EffectiveFrom, EffectiveTo = c.EffectiveTo
    };
}
