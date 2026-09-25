using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

public interface ICustomerContactService
{
    Task<IReadOnlyList<CustomerContactModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default);
    Task<CustomerContactSaveResult> CreateAsync(int customerId, SaveCustomerContactRequest request, CancellationToken ct = default);
    Task<CustomerContactSaveResult> UpdateAsync(long contactId, SaveCustomerContactRequest request, CancellationToken ct = default);
    Task<CustomerContactSaveResult> SetStatusAsync(long contactId, bool active, CancellationToken ct = default);
}

internal sealed class CustomerContactService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ICustomerLookup customerLookup) : ICustomerContactService
{
    public async Task<IReadOnlyList<CustomerContactModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var query = db.CustomerContacts.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId);
        if (!includeInactive) query = query.Where(c => c.Status == ActiveInactiveStatuses.Active);
        return await query.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name).Select(c => ToModel(c)).ToListAsync(ct);
    }

    public async Task<CustomerContactSaveResult> CreateAsync(int customerId, SaveCustomerContactRequest request, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var errors = Validate(request, null);
        if (errors.Count > 0) throw new ValidationException(errors);

        var contact = new CustomerContact
        {
            CustomerId = customerId, Name = request.Name.Trim(), Designation = Trim(request.Designation),
            Mobile1 = request.Mobile1.Trim(), Mobile2 = Trim(request.Mobile2), Telephone = Trim(request.Telephone),
            Email = Trim(request.Email), AvailabilityTime = Trim(request.AvailabilityTime),
            Purpose = request.Purpose is { Count: > 0 } p ? string.Join(",", p) : null,
            IsPrimary = request.IsPrimary, Status = ActiveInactiveStatuses.Active
        };

        if (request.IsPrimary)
            await db.CustomerContacts.Where(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId && c.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPrimary, false), ct);

        db.CustomerContacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return new CustomerContactSaveResult { Contact = ToModel(contact) };
    }

    public async Task<CustomerContactSaveResult> UpdateAsync(long contactId, SaveCustomerContactRequest request, CancellationToken ct = default)
    {
        var contact = await Find(contactId, ct);
        var errors = Validate(request, contactId);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (request.IsPrimary && !contact.IsPrimary)
            await db.CustomerContacts.Where(c => c.TenantId == tenant.TenantId && c.CustomerId == contact.CustomerId && c.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPrimary, false), ct);

        contact.Name = request.Name.Trim();
        contact.Designation = Trim(request.Designation);
        contact.Mobile1 = request.Mobile1.Trim();
        contact.Mobile2 = Trim(request.Mobile2);
        contact.Telephone = Trim(request.Telephone);
        contact.Email = Trim(request.Email);
        contact.AvailabilityTime = Trim(request.AvailabilityTime);
        contact.Purpose = request.Purpose is { Count: > 0 } p ? string.Join(",", p) : null;
        contact.IsPrimary = request.IsPrimary;
        await db.SaveChangesAsync(ct);
        return new CustomerContactSaveResult { Contact = ToModel(contact) };
    }

    public async Task<CustomerContactSaveResult> SetStatusAsync(long contactId, bool active, CancellationToken ct = default)
    {
        var contact = await Find(contactId, ct);
        var warnings = new List<string>();
        if (!active)
        {
            var otherActive = await db.CustomerContacts.CountAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == contact.CustomerId
                && c.CustomerContactId != contactId && c.Status == ActiveInactiveStatuses.Active, ct);
            // §11: "warning if the last one is deactivated" — never a block (BR-C2's own non-blocking pattern).
            if (otherActive == 0) warnings.Add("This is the customer's last active contact.");
            contact.IsPrimary = false;
        }
        contact.Status = active ? ActiveInactiveStatuses.Active : ActiveInactiveStatuses.Inactive;
        await db.SaveChangesAsync(ct);
        return new CustomerContactSaveResult { Contact = ToModel(contact), Warnings = warnings };
    }

    private async Task<CustomerContact> Find(long contactId, CancellationToken ct) =>
        await db.CustomerContacts.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerContactId == contactId, ct)
        ?? throw new NotFoundException($"Contact {contactId} was not found.");

    private List<ValidationError> Validate(SaveCustomerContactRequest request, long? existingContactId)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.Name)) Add("name", Msg.Required, ("Field", "Name"));
        if (string.IsNullOrWhiteSpace(request.Mobile1)) Add("mobile1", Msg.Required, ("Field", "Mobile"));
        else if (!CustomerFormats.IsMobile(request.Mobile1)) Add("mobile1", Msg.Format, ("Field", "Mobile"));
        if (!string.IsNullOrWhiteSpace(request.Mobile2) && !CustomerFormats.IsMobile(request.Mobile2)) Add("mobile2", Msg.Format, ("Field", "Second mobile"));
        if (!string.IsNullOrWhiteSpace(request.Telephone) && !CustomerFormats.IsPhone(request.Telephone)) Add("telephone", Msg.Format, ("Field", "Telephone"));
        if (!string.IsNullOrWhiteSpace(request.Email) && !CustomerFormats.IsEmail(request.Email)) Add("email", Msg.Email);
        if (request.Purpose is { Count: > 0 } purposes)
            foreach (var p in purposes)
                if (!ContactPurposes.All.Contains(p)) Add("purpose", Msg.OneOf, ("Field", "Purpose"), ("Allowed", string.Join(", ", ContactPurposes.All)));

        return errors;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerContactModel ToModel(CustomerContact c) => new()
    {
        CustomerContactId = c.CustomerContactId, CustomerId = c.CustomerId, Name = c.Name, Designation = c.Designation,
        Mobile1 = c.Mobile1, Mobile2 = c.Mobile2, Telephone = c.Telephone, Email = c.Email, AvailabilityTime = c.AvailabilityTime,
        Purpose = string.IsNullOrEmpty(c.Purpose) ? [] : c.Purpose.Split(',', StringSplitOptions.RemoveEmptyEntries),
        IsPrimary = c.IsPrimary, Status = c.Status
    };
}

/// <summary>Confirms a customer exists (and is this tenant's), shared by every one of its child services so each
/// one doesn't repeat the same lookup-or-404.</summary>
internal interface ICustomerLookup
{
    Task RequireAsync(int customerId, CancellationToken ct = default);
}

internal sealed class CustomerLookup(TripsDbContext db, ITenantContext tenant) : ICustomerLookup
{
    public async Task RequireAsync(int customerId, CancellationToken ct = default)
    {
        if (!await db.Customers.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct))
            throw new NotFoundException($"Customer {customerId} was not found.");
    }
}
