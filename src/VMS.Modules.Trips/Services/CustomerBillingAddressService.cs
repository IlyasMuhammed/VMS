using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

public interface ICustomerBillingAddressService
{
    Task<IReadOnlyList<CustomerBillingAddressModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default);
    Task<CustomerBillingAddressModel> CreateAsync(int customerId, SaveCustomerBillingAddressRequest request, CancellationToken ct = default);
    Task<CustomerBillingAddressModel> UpdateAsync(long addressId, SaveCustomerBillingAddressRequest request, CancellationToken ct = default);
    Task<CustomerBillingAddressModel> SetDefaultAsync(long addressId, CancellationToken ct = default);
    Task<CustomerBillingAddressModel> SetStatusAsync(long addressId, bool active, CancellationToken ct = default);
}

internal sealed class CustomerBillingAddressService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ILookupReader lookups, IOperatingClock clock, ICustomerLookup customerLookup)
    : ICustomerBillingAddressService
{
    public async Task<IReadOnlyList<CustomerBillingAddressModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var query = db.CustomerBillingAddresses.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.CustomerId == customerId);
        if (!includeInactive) query = query.Where(a => a.Status == ActiveInactiveStatuses.Active);
        return await query.OrderByDescending(a => a.IsDefault).ThenBy(a => a.AddressName).Select(a => ToModel(a)).ToListAsync(ct);
    }

    public async Task<CustomerBillingAddressModel> CreateAsync(int customerId, SaveCustomerBillingAddressRequest request, CancellationToken ct = default)
    {
        await customerLookup.RequireAsync(customerId, ct);
        var (errors, countryId, effectiveFrom) = await ValidateAsync(customerId, request, null, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var address = new CustomerBillingAddress
        {
            CustomerId = customerId, AddressName = request.AddressName.Trim(), AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = Trim(request.AddressLine2), CityId = request.CityId, ProvinceState = Trim(request.ProvinceState),
            CountryId = countryId, PostalCode = Trim(request.PostalCode), Ntn = Trim(request.Ntn), Strn = Trim(request.Strn),
            IsDefault = request.IsDefault, EffectiveFrom = effectiveFrom, EffectiveTo = request.EffectiveTo, Status = ActiveInactiveStatuses.Active
        };

        if (request.IsDefault)
            await db.CustomerBillingAddresses.Where(a => a.TenantId == tenant.TenantId && a.CustomerId == customerId && a.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);

        db.CustomerBillingAddresses.Add(address);
        await db.SaveChangesAsync(ct);
        return ToModel(address);
    }

    public async Task<CustomerBillingAddressModel> UpdateAsync(long addressId, SaveCustomerBillingAddressRequest request, CancellationToken ct = default)
    {
        var address = await Find(addressId, ct);
        var (errors, countryId, effectiveFrom) = await ValidateAsync(address.CustomerId, request, addressId, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (request.IsDefault && !address.IsDefault)
            await db.CustomerBillingAddresses.Where(a => a.TenantId == tenant.TenantId && a.CustomerId == address.CustomerId && a.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);

        address.AddressName = request.AddressName.Trim();
        address.AddressLine1 = request.AddressLine1.Trim();
        address.AddressLine2 = Trim(request.AddressLine2);
        address.CityId = request.CityId;
        address.ProvinceState = Trim(request.ProvinceState);
        address.CountryId = countryId;
        address.PostalCode = Trim(request.PostalCode);
        address.Ntn = Trim(request.Ntn);
        address.Strn = Trim(request.Strn);
        address.IsDefault = request.IsDefault;
        address.EffectiveFrom = effectiveFrom;
        address.EffectiveTo = request.EffectiveTo;
        await db.SaveChangesAsync(ct);
        return ToModel(address);
    }

    public async Task<CustomerBillingAddressModel> SetDefaultAsync(long addressId, CancellationToken ct = default)
    {
        var address = await Find(addressId, ct);
        if (address.Status != ActiveInactiveStatuses.Active) throw new ConflictException("Only an Active address can be made the default.");
        await db.CustomerBillingAddresses.Where(a => a.TenantId == tenant.TenantId && a.CustomerId == address.CustomerId && a.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
        address.IsDefault = true;
        await db.SaveChangesAsync(ct);
        return ToModel(address);
    }

    public async Task<CustomerBillingAddressModel> SetStatusAsync(long addressId, bool active, CancellationToken ct = default)
    {
        var address = await Find(addressId, ct);
        address.Status = active ? ActiveInactiveStatuses.Active : ActiveInactiveStatuses.Inactive;
        if (!active) address.IsDefault = false;
        await db.SaveChangesAsync(ct);
        return ToModel(address);
    }

    private async Task<CustomerBillingAddress> Find(long addressId, CancellationToken ct) =>
        await db.CustomerBillingAddresses.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.CustomerBillingAddressId == addressId, ct)
        ?? throw new NotFoundException($"Billing address {addressId} was not found.");

    private async Task<(List<ValidationError> Errors, int CountryId, DateOnly EffectiveFrom)> ValidateAsync(
        int customerId, SaveCustomerBillingAddressRequest request, long? existingAddressId, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.AddressName)) Add("addressName", Msg.Required, ("Field", "Address name"));
        else if (await db.CustomerBillingAddresses.AnyAsync(a => a.TenantId == tenant.TenantId && a.CustomerId == customerId
                     && a.AddressName == request.AddressName.Trim() && a.CustomerBillingAddressId != (existingAddressId ?? 0), ct))
            Add("addressName", Msg.AlreadyUsed, ("Field", "address name"), ("Code", request.AddressName.Trim()), ("Name", "another address on this customer"));

        if (string.IsNullOrWhiteSpace(request.AddressLine1)) Add("addressLine1", Msg.Required, ("Field", "Address line 1"));
        if (request.CityId <= 0) Add("cityId", Msg.Required, ("Field", "City"));
        else if (!await lookups.IsActiveAsync(PlatformLookups.City, request.CityId)) Add("cityId", Msg.Invalid, ("Field", "City"));

        var countryId = request.CountryId ?? 0;
        if (request.CountryId is { } given)
        {
            if (await lookups.FindAsync(PlatformLookups.Country, given) is null) Add("countryId", Msg.Invalid, ("Field", "Country"));
        }
        else
        {
            var pakistan = (await lookups.GetActiveAsync(PlatformLookups.Country)).FirstOrDefault(c => c.Code == "PAKISTAN");
            if (pakistan is null) Add("countryId", Msg.Required, ("Field", "Country")); else countryId = pakistan.Id;
        }

        var effectiveFrom = request.EffectiveFrom ?? await clock.TodayAsync(tenant.TenantId);
        if (request.EffectiveTo is { } to && to < effectiveFrom) Add("effectiveTo", Msg.Min, ("Field", "Effective to"), ("Min", effectiveFrom.ToString("yyyy-MM-dd")));

        return (errors, countryId, effectiveFrom);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerBillingAddressModel ToModel(CustomerBillingAddress a) => new()
    {
        CustomerBillingAddressId = a.CustomerBillingAddressId, CustomerId = a.CustomerId, AddressName = a.AddressName,
        AddressLine1 = a.AddressLine1, AddressLine2 = a.AddressLine2, CityId = a.CityId, ProvinceState = a.ProvinceState,
        CountryId = a.CountryId, PostalCode = a.PostalCode, Ntn = a.Ntn, Strn = a.Strn, IsDefault = a.IsDefault,
        EffectiveFrom = a.EffectiveFrom, EffectiveTo = a.EffectiveTo, Status = a.Status
    };
}
