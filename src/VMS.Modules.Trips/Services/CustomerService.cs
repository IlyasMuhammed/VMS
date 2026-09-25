using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Services;

public sealed class CustomerPickerItem
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

public interface ICustomerService
{
    Task<PaginatedResponse<CustomerModel>> ListAsync(string? search, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerPickerItem>> PickerAsync(string? search, CancellationToken ct = default);
    Task<CustomerModel> GetAsync(int customerId, CancellationToken ct = default);
    Task<CustomerModel> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default);
    Task<CustomerModel> UpdateAsync(int customerId, SaveCustomerRequest request, CancellationToken ct = default);
    Task<ActivationCheckModel> ActivationCheckAsync(int customerId, CancellationToken ct = default);
    Task<CustomerModel> ActivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default);
    Task<CustomerModel> DeactivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default);
    Task<CustomerModel> ReactivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default);
    Task DeleteAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default);
    Task<CustomerHistory> HistoryAsync(int customerId, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct = default);
}

internal sealed class CustomerService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ILookupReader lookups,
    INumberSeries series, ICurrencySeeder currencySeeder, IEnumerable<ICustomerActivationRequirement> requirements) : ICustomerService
{
    public async Task<PaginatedResponse<CustomerModel>> ListAsync(string? search, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.CustomerName.Contains(term) || c.CustomerCode.Contains(term));
        }

        var total = await query.CountAsync(ct);
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 25 : Math.Min(pageSize, 200);
        var rows = await query.OrderBy(c => c.CustomerName)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(c => ToModel(c)).ToListAsync(ct);
        return new PaginatedResponse<CustomerModel> { Items = rows, TotalCount = total, Page = page, PageSize = pageSize };
    }

    /// <summary>Active customers only (FSD AC-03: an Inactive customer "is not listed" when starting new work,
    /// e.g. a new trip) — the search screen uses <see cref="ListAsync"/> instead, which shows every status.</summary>
    public async Task<IReadOnlyList<CustomerPickerItem>> PickerAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.Status == CustomerStatuses.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.CustomerName.Contains(term) || c.CustomerCode.Contains(term));
        }
        return await query.OrderBy(c => c.CustomerName).Take(50)
            .Select(c => new CustomerPickerItem { CustomerId = c.CustomerId, CustomerCode = c.CustomerCode, CustomerName = c.CustomerName })
            .ToListAsync(ct);
    }

    public async Task<CustomerModel> GetAsync(int customerId, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        return ToModel(customer);
    }

    public async Task<CustomerModel> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        await currencySeeder.EnsureSeededAsync(ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.CustomerName)) Add("customerName", Msg.Required, ("Field", "Customer name"));
        // Duplicate name is a warning only, per §10 — never blocks the save, so no check is made here.
        if (string.IsNullOrWhiteSpace(request.AddressLine1)) Add("addressLine1", Msg.Required, ("Field", "Address line 1"));

        string? code = null;
        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
        {
            code = request.CustomerCode.Trim().ToUpperInvariant();
            if (code.Length > 20 || !CustomerCodePattern().IsMatch(code)) Add("customerCode", Msg.Invalid, ("Field", "Customer code"));
            else if (await db.Customers.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerCode == code, ct))
                Add("customerCode", Msg.AlreadyUsed, ("Field", "customer code"), ("Code", code), ("Name",
                    (await db.Customers.Where(c => c.TenantId == tenant.TenantId && c.CustomerCode == code).Select(c => c.CustomerName).FirstAsync(ct))));
        }

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "PKR" : request.CurrencyCode.Trim().ToUpperInvariant();
        if (!await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == currencyCode, ct))
            Add("currencyCode", Msg.TrpExchangeRateCurrencyUnknown, ("Code", currencyCode));

        int countryId;
        if (request.CountryId is { } givenCountry)
        {
            if (await lookups.FindAsync(PlatformLookups.Country, givenCountry) is null) Add("countryId", Msg.Invalid, ("Field", "Country"));
            countryId = givenCountry;
        }
        else
        {
            var pakistan = (await lookups.GetActiveAsync(PlatformLookups.Country)).FirstOrDefault(c => c.Code == "PAKISTAN");
            if (pakistan is null) { Add("countryId", Msg.Required, ("Field", "Country")); countryId = 0; }
            else countryId = pakistan.Id;
        }

        if (request.CityId is { } cityId && !await lookups.IsActiveAsync(PlatformLookups.City, cityId))
            Add("cityId", Msg.Invalid, ("Field", "City"));

        var paymentTermsDays = request.PaymentTermsDays ?? 30;
        if (paymentTermsDays is < 0 or > 365) Add("paymentTermsDays", Msg.Max, ("Field", "Payment terms (days)"), ("Max", "365"));

        if (errors.Count > 0) throw new ValidationException(errors);

        var customer = await db.InTransactionAsync(async ct2 =>
        {
            var c = new Customer
            {
                CustomerCode = code ?? await series.NextAsync(db, NumberSeriesCodes.Customer, cancellationToken: ct2),
                CustomerName = request.CustomerName.Trim(),
                ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim(),
                AddressLine1 = request.AddressLine1.Trim(),
                AddressLine2 = string.IsNullOrWhiteSpace(request.AddressLine2) ? null : request.AddressLine2.Trim(),
                CountryId = countryId,
                ProvinceState = string.IsNullOrWhiteSpace(request.ProvinceState) ? null : request.ProvinceState.Trim(),
                CityId = request.CityId,
                PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim(),
                Ntn = string.IsNullOrWhiteSpace(request.Ntn) ? null : request.Ntn.Trim(),
                Strn = string.IsNullOrWhiteSpace(request.Strn) ? null : request.Strn.Trim(),
                OtherRegistrationNo = string.IsNullOrWhiteSpace(request.OtherRegistrationNo) ? null : request.OtherRegistrationNo.Trim(),
                CurrencyCode = currencyCode,
                PaymentTermsDays = paymentTermsDays,
                CreditLimit = request.CreditLimit,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                Status = CustomerStatuses.Draft
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync(ct2);
            return c;
        }, ct);

        return ToModel(customer);
    }

    public async Task<CustomerModel> UpdateAsync(int customerId, SaveCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.CustomerName)) Add("customerName", Msg.Required, ("Field", "Customer name"));
        if (string.IsNullOrWhiteSpace(request.AddressLine1)) Add("addressLine1", Msg.Required, ("Field", "Address line 1"));
        if (string.IsNullOrWhiteSpace(request.RowVersion)) Add("rowVersion", Msg.Required, ("Field", "Row version"));

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? customer.CurrencyCode : request.CurrencyCode.Trim().ToUpperInvariant();
        if (currencyCode != customer.CurrencyCode && !await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == currencyCode, ct))
            Add("currencyCode", Msg.TrpExchangeRateCurrencyUnknown, ("Code", currencyCode));
        // BR-C3: changing the currency is blocked once any invoice exists — no Invoice table exists yet in this
        // module, so nothing to check today; extend here once one does.

        if (request.CountryId is { } countryId && await lookups.FindAsync(PlatformLookups.Country, countryId) is null)
            Add("countryId", Msg.Invalid, ("Field", "Country"));
        if (request.CityId is { } cityId && !await lookups.IsActiveAsync(PlatformLookups.City, cityId))
            Add("cityId", Msg.Invalid, ("Field", "City"));

        var paymentTermsDays = request.PaymentTermsDays ?? customer.PaymentTermsDays;
        if (paymentTermsDays is < 0 or > 365) Add("paymentTermsDays", Msg.Max, ("Field", "Payment terms (days)"), ("Max", "365"));
        if (errors.Count > 0) throw new ValidationException(errors);

        try { db.Entry(customer).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion!); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        customer.CustomerName = request.CustomerName.Trim();
        customer.ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim();
        customer.AddressLine1 = request.AddressLine1.Trim();
        customer.AddressLine2 = string.IsNullOrWhiteSpace(request.AddressLine2) ? null : request.AddressLine2.Trim();
        if (request.CountryId is { } newCountry) customer.CountryId = newCountry;
        customer.ProvinceState = string.IsNullOrWhiteSpace(request.ProvinceState) ? null : request.ProvinceState.Trim();
        customer.CityId = request.CityId;
        customer.PostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim();
        customer.Ntn = string.IsNullOrWhiteSpace(request.Ntn) ? null : request.Ntn.Trim();
        customer.Strn = string.IsNullOrWhiteSpace(request.Strn) ? null : request.Strn.Trim();
        customer.OtherRegistrationNo = string.IsNullOrWhiteSpace(request.OtherRegistrationNo) ? null : request.OtherRegistrationNo.Trim();
        customer.CurrencyCode = currencyCode;
        customer.PaymentTermsDays = paymentTermsDays;
        customer.CreditLimit = request.CreditLimit;
        customer.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(customerId, ct); }
        return ToModel(customer);
    }

    public async Task<ActivationCheckModel> ActivationCheckAsync(int customerId, CancellationToken ct = default)
    {
        await Find(customerId, ct);
        var missing = new List<string>();
        foreach (var requirement in requirements)
        {
            var reason = await requirement.CheckAsync(customerId, ct);
            if (reason is not null) missing.Add(reason);
        }
        return new ActivationCheckModel { CanActivate = missing.Count == 0, MissingItems = missing };
    }

    public async Task<CustomerModel> ActivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        if (customer.Status != CustomerStatuses.Draft)
            throw new ConflictException($"A {customer.Status} customer cannot be activated; only a Draft customer can.");

        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        var check = await ActivationCheckAsync(customerId, ct);
        // Admin can activate with an override reason even when the checklist has not passed (§10).
        if (!check.CanActivate && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(check.MissingItems.Select(m => messages.Error(null, Msg.Invalid, ("Field", m))).ToList());

        try { db.Entry(customer).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        customer.Status = CustomerStatuses.Active;
        customer.InactiveReason = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(customerId, ct); }
        return ToModel(customer);
    }

    public async Task<CustomerModel> DeactivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        if (customer.Status == CustomerStatuses.Inactive) throw new ConflictException("The customer is already Inactive.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        try { db.Entry(customer).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // BR-C2: deactivation is allowed even with open trips or unpaid invoices — the caller sees a warning
        // listing them (none of those tables exist in this module yet, so the list is always empty for now).
        customer.Status = CustomerStatuses.Inactive;
        customer.InactiveReason = request.Reason.Trim();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(customerId, ct); }
        return ToModel(customer);
    }

    public async Task<CustomerModel> ReactivateAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        if (customer.Status != CustomerStatuses.Inactive) throw new ConflictException("Only an Inactive customer can be reactivated.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        try { db.Entry(customer).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        customer.Status = CustomerStatuses.Active;
        customer.InactiveReason = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(customerId, ct); }
        return ToModel(customer);
    }

    public async Task DeleteAsync(int customerId, ChangeCustomerStatusRequest request, CancellationToken ct = default)
    {
        var customer = await Find(customerId, ct);
        if (customer.Status != CustomerStatuses.Draft) throw new ConflictException("Only a never-activated Draft customer can be deleted.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        try { db.Entry(customer).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        db.Customers.Remove(customer);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(customerId, ct); }
    }

    public async Task<CustomerHistory> HistoryAsync(int customerId, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await Find(customerId, ct);
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 100);
        var root = customerId.ToString();

        var changes = db.Set<AuditEntry>().AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Customer" && a.RootRecordId == root);
        var total = await changes.CountAsync(ct);
        var rows = await changes.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.AuditEntryID)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = rows.Select(a =>
        {
            var hidden = a.RequiredPermission is { } permission && !user.HasPermission(permission);
            return new CustomerHistoryChange
            {
                Id = a.AuditEntryID, OccurredAt = a.OccurredAt, GroupId = a.GroupId, UserName = a.UserName, Entity = a.Entity, RecordId = a.RecordId,
                Action = a.Action, Field = a.Field, Reason = a.Reason, Restricted = hidden, OldValue = hidden ? null : a.OldValue, NewValue = hidden ? null : a.NewValue
            };
        }).ToList();

        return new CustomerHistory { Changes = new PaginatedResponse<CustomerHistoryChange> { Items = items, TotalCount = total, Page = page, PageSize = pageSize } };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private async Task<Customer> Find(int customerId, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct)
        ?? throw new NotFoundException($"Customer {customerId} was not found.");

    private async Task<Exception> StaleAsync(int customerId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "Customer" && a.RootRecordId == customerId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }

    private static readonly System.Text.RegularExpressions.Regex _codePattern = new("^[A-Z0-9-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static System.Text.RegularExpressions.Regex CustomerCodePattern() => _codePattern;

    private static CustomerModel ToModel(Customer c) => new()
    {
        CustomerId = c.CustomerId, CustomerCode = c.CustomerCode, CustomerName = c.CustomerName, ShortName = c.ShortName,
        AddressLine1 = c.AddressLine1, AddressLine2 = c.AddressLine2, CountryId = c.CountryId, ProvinceState = c.ProvinceState,
        CityId = c.CityId, PostalCode = c.PostalCode, Ntn = c.Ntn, Strn = c.Strn, OtherRegistrationNo = c.OtherRegistrationNo,
        CurrencyCode = c.CurrencyCode, PaymentTermsDays = c.PaymentTermsDays, CreditLimit = c.CreditLimit,
        Status = c.Status, InactiveReason = c.InactiveReason, Remarks = c.Remarks, RowVersion = Convert.ToBase64String(c.RowVersion)
    };
}
