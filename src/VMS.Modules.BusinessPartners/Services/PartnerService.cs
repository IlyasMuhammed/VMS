using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.BusinessPartners.Data;
using VMS.Modules.BusinessPartners.Domain;
using VMS.Modules.BusinessPartners.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Branches;
using VMS.Shared.Common;
using VMS.Shared.Documents;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;
using VMS.Shared.Time;

namespace VMS.Modules.BusinessPartners.Services;

/// <summary>Who is acting, and which restricted values they may see, for the rules that depend on it.</summary>
public sealed record PartnerCaller(int UserId, string? UserName, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
}

public interface IPartnerService
{
    Task<PartnerModel> GetAsync(int id);
    Task<PartnerModel> CreateAsync(CreatePartnerRequest request, PartnerCaller caller);
    Task<PartnerModel> UpdateAsync(int id, UpdatePartnerRequest request, PartnerCaller caller);
    Task<PartnerModel> AddRoleAsync(int id, AddRoleRequest request, PartnerCaller caller);
    Task<PartnerModel> RemoveRoleAsync(int id, string roleCode, string? reason, PartnerCaller caller);
    Task<PartnerModel> ChangeStatusAsync(int id, ChangeStatusRequest request, PartnerCaller caller);
    Task<List<PartnerUsageModel>> UsageAsync(int id, PartnerIntent intent, string? roleCode);
    Task<DuplicateCheckResult> CheckDuplicatesAsync(DuplicateCheckRequest request);
}

/// <summary>Business partners: creating, changing, giving and taking away roles, and changing status (FSD §5 to §13).</summary>
internal sealed class PartnerService(
    PartnerDbContext db,
    ITenantContext tenantContext,
    IAuditContext audit,
    INumberSeries series,
    IOperatingClock clock,
    ILookupReader lookups,
    IMessageCatalogue messages,
    DuplicateService duplicates,
    IBranchDirectory branches,
    IDocumentCheck documents,
    IEnumerable<IPartnerUsageCheck> usageChecks) : IPartnerService
{
    private Guid Tenant => tenantContext.TenantId;
    private PartnerRules Rules => new(messages);

    private IQueryable<BusinessPartner> Own() => db.Partners.Where(p => p.TenantId == Tenant && !p.IsDeleted);

    private IQueryable<BusinessPartner> WithEverything(IQueryable<BusinessPartner> q) =>
        q.Include(p => p.Roles).Include(p => p.Contacts).Include(p => p.Addresses).Include(p => p.BankAccounts)
         .Include(p => p.Driver).Include(p => p.Vendor).Include(p => p.Customer).AsSplitQuery();

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<PartnerModel> GetAsync(int id)
    {
        var partner = await WithEverything(Own().AsNoTracking()).FirstOrDefaultAsync(p => p.BusinessPartnerId == id)
            ?? throw new NotFoundException("Partner not found.");
        var model = ToModel(partner);
        // BR-BP-005 (OQ-04: warn, never block): a document a held role expects but has not been attached.
        if (documents.CanCheck)
        {
            var roles = partner.Roles.Where(r => r.IsActive).Select(r => r.RoleCode).ToHashSet();
            var missing = await documents.MissingAsync("BusinessPartner", id, roles);
            model.DocumentWarnings = missing.Select(m => $"{m.TypeName} has not been uploaded.").ToList();
        }
        return model;
    }

    public async Task<DuplicateCheckResult> CheckDuplicatesAsync(DuplicateCheckRequest request) =>
        new() { Matches = await duplicates.FindAsync(request) };

    // ── Creating ────────────────────────────────────────────────────────────────────

    public async Task<PartnerModel> CreateAsync(CreatePartnerRequest request, PartnerCaller caller)
    {
        PartnerRules.Normalize(request);
        var roles = request.Roles.Select(r => r?.Trim() ?? string.Empty).Where(r => r.Length > 0).Distinct().ToList();
        var today = await clock.TodayAsync(Tenant);

        var errors = Rules.Validate(request, roles, creating: true, today);
        if (roles.Count == 0) errors.Add(messages.Error("roles", Msg.BpRoleRequired));
        foreach (var unknown in roles.Where(r => !PartnerRoles.All.Contains(r)))
            errors.Add(messages.Error("roles", Msg.OneOf, ("Field", "Role"), ("Allowed", string.Join(", ", PartnerRoles.All))));
        if (request.Addresses.Any(a => a.IsPrimary)) errors.Add(messages.Error("addresses", Msg.OnlyOnePrimary, ("Field", "address")));
        errors.AddRange(await LookupErrorsAsync(request, existing: null));
        if (errors.Count > 0) throw new ValidationException(errors);

        errors.AddRange(await duplicates.UniquenessErrorsAsync(request, excludeId: null, roles));
        var (unacknowledged, acknowledged) = await duplicates.CheckAcknowledgementAsync(request, excludeId: null);
        errors.AddRange(unacknowledged);
        if (errors.Count > 0) throw new ValidationException(errors);

        var cities = await CitiesAsync(request);
        var created = await SaveNewAsync(request, roles, caller, today, cities, acknowledged);
        return await GetAsync(created);
    }

    private async Task<int> SaveNewAsync(CreatePartnerRequest request, List<string> roles, PartnerCaller caller, DateOnly today,
        IReadOnlyDictionary<int, LookupItem> cities, List<DuplicateMatch> acknowledged)
    {
        try
        {
            return await db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;
                var logRoles = new List<string>();
                var partner = new BusinessPartner
                {
                    BpCode = await series.NextAsync(db, NumberSeriesCodes.BusinessPartner, today, Tenant, ct),
                    PartyType = request.PartyType,
                    LegalName = request.LegalName,
                    DisplayName = request.DisplayName ?? Truncate(request.LegalName, 60),
                    Cnic = request.Cnic,
                    Ntn = request.Ntn,
                    Strn = request.Strn,
                    FilerStatus = request.FilerStatus ?? FilerStatuses.Unknown,
                    PrimaryMobile = request.PrimaryMobile,
                    AlternatePhone = request.AlternatePhone,
                    Email = request.Email,
                    CityId = request.CityId,
                    AddressLine = request.AddressLine,
                    BranchId = request.BranchId,
                    Notes = request.Notes,
                    Status = PartnerStatuses.Active,
                    CreatedBy = caller.UserId,
                    CreatedOn = now,
                    ModifiedOn = now
                };

                // What the caller may not see they may not set: opening balance needs its permission.
                if (caller.Has(PermissionCodes.BP_FIELD_OPENING_VIEW))
                {
                    partner.OpeningBalance = request.OpeningBalance ?? 0m;
                    partner.OpeningBalanceDate = request.OpeningBalance is null or 0m ? null : request.OpeningBalanceDate;
                }

                foreach (var role in roles)
                {
                    partner.Roles.Add(new BusinessPartnerRole { RoleCode = role, EffectiveFrom = today });
                    logRoles.Add(role);
                }
                ApplyDetails(partner, request.Driver, request.Vendor, request.Customer, roles, caller, existing: null);

                // The General tab's address is the primary Registered address (FSD §8.2).
                partner.Addresses.Add(new BpAddress
                {
                    AddressType = AddressTypes.Registered, Line1 = request.AddressLine, CityId = request.CityId,
                    ProvinceCode = ProvinceOf(cities, request.CityId), IsPrimary = true
                });
                foreach (var a in request.Addresses) partner.Addresses.Add(NewAddress(a, cities));
                foreach (var c in request.Contacts) partner.Contacts.Add(NewContact(c));
                foreach (var b in request.BankAccounts) partner.BankAccounts.Add(NewBankAccount(b));

                db.Partners.Add(partner);
                await db.SaveChangesAsync(ct);
                foreach (var role in logRoles) db.RoleLog.Add(RoleLogRow(partner.BusinessPartnerId, role, "Added", today, null, caller, now));
                if (logRoles.Count > 0) await db.SaveChangesAsync(ct);

                // Each duplicate the person chose to save alongside is on record, with who and when (BR-BP-021).
                foreach (var match in acknowledged)
                    audit.Note(new AuditNote(nameof(BusinessPartner), partner.BusinessPartnerId.ToString(), "DuplicateOverridden", NewValue: $"{match.BpCode} {match.LegalName} ({match.MatchType})", Reason: match.MatchType,
                        RootEntity: nameof(BusinessPartner), RootRecordId: partner.BusinessPartnerId.ToString()));
                if (acknowledged.Count > 0) await db.SaveChangesAsync(ct);

                return partner.BusinessPartnerId;
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Two people saved the same CNIC or NTN at once: the unique index caught it. Say so as the friendly message.
            throw await UniqueViolationAsync(request, excludeId: null, ex);
        }
    }

    // ── Updating ────────────────────────────────────────────────────────────────────

    public async Task<PartnerModel> UpdateAsync(int id, UpdatePartnerRequest request, PartnerCaller caller)
    {
        var partner = await WithEverything(Own()).FirstOrDefaultAsync(p => p.BusinessPartnerId == id)
            ?? throw new NotFoundException("Partner not found.");
        if (partner.Status == PartnerStatuses.Merged) throw new ConflictException("This partner was merged into another and is read-only.");

        PartnerRules.Normalize(request);
        var activeRoles = partner.Roles.Where(r => r.IsActive).Select(r => r.RoleCode).ToList();
        var today = await clock.TodayAsync(Tenant);

        var errors = Rules.Validate(request, activeRoles, creating: false, today);
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
        if (request.Addresses.Any(a => a.IsPrimary)) errors.Add(messages.Error("addresses", Msg.OnlyOnePrimary, ("Field", "address")));
        errors.AddRange(await LookupErrorsAsync(request, partner));
        if (partyTypeChanged(partner, request) && (await UsageAsync(id, PartnerIntent.ChangePartyType, null)).Count > 0)
            errors.Add(messages.Error("partyType", Msg.ReadOnlyOnceSaved, ("Field", "Party type")));
        if (errors.Count > 0) throw new ValidationException(errors);

        errors.AddRange(await duplicates.UniquenessErrorsAsync(request, id, activeRoles));
        List<DuplicateMatch> acknowledged = [];
        // Soft duplicates are asked about when the fields that cause them changed, not on every edit of an old record.
        if (partner.LegalName != request.LegalName || partner.PrimaryMobile != request.PrimaryMobile || partner.CityId != request.CityId)
        {
            var (unacknowledged, ok) = await duplicates.CheckAcknowledgementAsync(request, id);
            errors.AddRange(unacknowledged);
            acknowledged = ok;
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        try { db.Entry(partner).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        var cities = await CitiesAsync(request);
        try
        {
            await db.InTransactionAsync(async ct =>
            {
                Apply(partner, request, activeRoles, caller, cities);
                partner.ModifiedBy = caller.UserId;
                partner.ModifiedOn = DateTime.UtcNow;

                // A new primary must not be saved before the old one is cleared: the database allows one at a time.
                if (ClearMovingPrimaries(partner, request)) await db.SaveChangesAsync(ct);

                SyncChildren(partner, request, cities);
                await db.SaveChangesAsync(ct);

                foreach (var match in acknowledged)
                    audit.Note(new AuditNote(nameof(BusinessPartner), id.ToString(), "DuplicateOverridden", NewValue: $"{match.BpCode} {match.LegalName} ({match.MatchType})", Reason: match.MatchType,
                        RootEntity: nameof(BusinessPartner), RootRecordId: id.ToString()));
                if (acknowledged.Count > 0) await db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            throw await StaleAsync(id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw await UniqueViolationAsync(request, id, ex);
        }

        return await GetAsync(id);

        static bool partyTypeChanged(BusinessPartner p, PartnerInput i) => p.PartyType != i.PartyType && PartyTypes.All.Contains(i.PartyType);
    }

    private void Apply(BusinessPartner p, PartnerInput i, List<string> activeRoles, PartnerCaller caller, IReadOnlyDictionary<int, LookupItem> cities)
    {
        p.PartyType = i.PartyType;
        p.LegalName = i.LegalName;
        p.DisplayName = i.DisplayName ?? Truncate(i.LegalName, 60);
        p.Cnic = i.Cnic;
        p.Ntn = i.Ntn;
        p.Strn = i.Strn;
        p.FilerStatus = i.FilerStatus ?? FilerStatuses.Unknown;
        p.PrimaryMobile = i.PrimaryMobile;
        p.AlternatePhone = i.AlternatePhone;
        p.Email = i.Email;
        p.CityId = i.CityId;
        p.AddressLine = i.AddressLine;
        p.BranchId = i.BranchId;
        p.Notes = i.Notes;
        // Opening balance is entered once, at creation (BR-BP-018): an update never changes it.

        ApplyDetails(p, i.Driver, i.Vendor, i.Customer, activeRoles, caller, existing: p);

        var primary = p.Addresses.FirstOrDefault(a => a.IsPrimary);
        if (primary is not null)
        {
            primary.Line1 = i.AddressLine;
            primary.CityId = i.CityId;
            primary.ProvinceCode = ProvinceOf(cities, i.CityId);
        }
    }

    // ── Role panels ─────────────────────────────────────────────────────────────────

    /// <summary>Creates or updates the panel of each role the partner holds. Values the caller may not see are left as they were.</summary>
    private static void ApplyDetails(BusinessPartner p, DriverModel? driver, VendorModel? vendor, CustomerModel? customer,
        IReadOnlyCollection<string> roles, PartnerCaller caller, BusinessPartner? existing)
    {
        var salary = caller.Has(PermissionCodes.BP_FIELD_SALARY_VIEW);
        var credit = caller.Has(PermissionCodes.BP_FIELD_CREDIT_VIEW);

        if (roles.Contains(PartnerRoles.Driver) && driver is not null)
        {
            var d = p.Driver ??= new BpDriverDetail();
            d.LicenceNo = driver.LicenceNo;
            d.LicenceType = driver.LicenceType;
            d.LicenceIssueDate = driver.LicenceIssueDate;
            d.LicenceExpiryDate = driver.LicenceExpiryDate ?? default;
            d.EmploymentType = driver.EmploymentType;
            d.DateOfJoining = driver.DateOfJoining;
            d.BloodGroup = driver.BloodGroup;
            d.EmergencyContactName = driver.EmergencyContactName;
            d.EmergencyContactPhone = driver.EmergencyContactPhone;
            d.Guarantor = driver.Guarantor;
            d.DriverAppAccess = driver.DriverAppAccess;
            if (salary)
            {
                d.MonthlyRate = driver.MonthlyRate;
                d.CommissionBasis = driver.CommissionBasis ?? RoleFieldValues.CommissionNone;
                d.CommissionValue = d.CommissionBasis == RoleFieldValues.CommissionNone ? null : driver.CommissionValue;
            }
        }

        if (roles.Contains(PartnerRoles.Vendor) && vendor is not null)
        {
            var v = p.Vendor ??= new BpVendorDetail();
            v.SupplyCategories = string.Join(',', vendor.SupplyCategories.Distinct());
            if (credit)
            {
                v.PaymentTermDays = vendor.PaymentTermDays ?? 0;
                v.CreditLimit = vendor.CreditLimit;
            }
        }

        if (roles.Contains(PartnerRoles.Customer) && customer is not null)
        {
            var c = p.Customer ??= new BpCustomerDetail();
            c.CustomerType = customer.CustomerType;
            c.BillingCycle = customer.BillingCycle;
            c.RateBasis = customer.RateBasis;
            c.DefaultRate = customer.DefaultRate;
            if (credit)
            {
                c.CreditLimit = customer.CreditLimit;
                c.CreditDays = customer.CreditDays;
            }
        }
    }

    // ── Child collections ───────────────────────────────────────────────────────────

    private static BpContact NewContact(ContactModel c) => new()
    {
        ContactName = c.ContactName, Designation = c.Designation, Mobile = c.Mobile, Email = c.Email, IsPrimary = c.IsPrimary, Notes = c.Notes
    };

    private static BpAddress NewAddress(AddressModel a, IReadOnlyDictionary<int, LookupItem> cities) => new()
    {
        AddressType = a.AddressType, Line1 = a.Line1, Line2 = a.Line2, CityId = a.CityId, ProvinceCode = ProvinceOf(cities, a.CityId), Landmark = a.Landmark, IsPrimary = false
    };

    private static BpBankAccount NewBankAccount(BankAccountModel b) => new()
    {
        AccountTitle = b.AccountTitle, BankName = b.BankName, BranchCode = b.BranchCode, AccountNumber = b.AccountNumber, Iban = b.Iban, IsPrimary = b.IsPrimary
    };

    /// <summary>True if a contact or bank account is about to become primary while another still is, so the old one must be cleared first.</summary>
    private static bool ClearMovingPrimaries(BusinessPartner p, PartnerInput i)
    {
        var changed = false;

        var newContact = i.Contacts.FirstOrDefault(c => c.IsPrimary);
        foreach (var existing in p.Contacts.Where(c => c.IsPrimary && (newContact is null || newContact.Id != c.BpContactId)))
        { existing.IsPrimary = false; changed = true; }

        var newBank = i.BankAccounts.FirstOrDefault(b => b.IsPrimary);
        foreach (var existing in p.BankAccounts.Where(b => b.IsPrimary && (newBank is null || newBank.Id != b.BpBankAccountId)))
        { existing.IsPrimary = false; changed = true; }

        return changed;
    }

    /// <summary>Rows with an id are updated, rows without are added, rows left out are removed (FR-BP-010: the screen saves them all together).</summary>
    private void SyncChildren(BusinessPartner p, PartnerInput i, IReadOnlyDictionary<int, LookupItem> cities)
    {
        var unknown = new List<ValidationError>();

        // The primary address is the General tab's; the rest are the additional addresses.
        var extras = p.Addresses.Where(a => !a.IsPrimary).ToList();
        for (var n = 0; n < i.Addresses.Count; n++)
        {
            var a = i.Addresses[n];
            var row = a.Id is null ? null : extras.FirstOrDefault(x => x.BpAddressId == a.Id);
            if (a.Id is not null && row is null) { unknown.Add(messages.Error($"addresses[{n}].id", Msg.Invalid, ("Field", "Address"))); continue; }
            if (row is null) p.Addresses.Add(NewAddress(a, cities));
            else
            {
                row.AddressType = a.AddressType; row.Line1 = a.Line1; row.Line2 = a.Line2; row.CityId = a.CityId;
                row.ProvinceCode = ProvinceOf(cities, a.CityId); row.Landmark = a.Landmark;
            }
        }
        foreach (var gone in extras.Where(x => i.Addresses.All(a => a.Id != x.BpAddressId)).ToList()) p.Addresses.Remove(gone);

        for (var n = 0; n < i.Contacts.Count; n++)
        {
            var c = i.Contacts[n];
            var row = c.Id is null ? null : p.Contacts.FirstOrDefault(x => x.BpContactId == c.Id);
            if (c.Id is not null && row is null) { unknown.Add(messages.Error($"contacts[{n}].id", Msg.Invalid, ("Field", "Contact"))); continue; }
            if (row is null) p.Contacts.Add(NewContact(c));
            else
            {
                row.ContactName = c.ContactName; row.Designation = c.Designation; row.Mobile = c.Mobile;
                row.Email = c.Email; row.IsPrimary = c.IsPrimary; row.Notes = c.Notes;
            }
        }
        foreach (var gone in p.Contacts.Where(x => x.BpContactId != 0 && i.Contacts.All(c => c.Id != x.BpContactId)).ToList()) p.Contacts.Remove(gone);

        for (var n = 0; n < i.BankAccounts.Count; n++)
        {
            var b = i.BankAccounts[n];
            var row = b.Id is null ? null : p.BankAccounts.FirstOrDefault(x => x.BpBankAccountId == b.Id);
            if (b.Id is not null && row is null) { unknown.Add(messages.Error($"bankAccounts[{n}].id", Msg.Invalid, ("Field", "Bank account"))); continue; }
            if (row is null) p.BankAccounts.Add(NewBankAccount(b));
            else
            {
                row.AccountTitle = b.AccountTitle; row.BankName = b.BankName; row.BranchCode = b.BranchCode;
                row.AccountNumber = b.AccountNumber; row.Iban = b.Iban; row.IsPrimary = b.IsPrimary;
            }
        }
        foreach (var gone in p.BankAccounts.Where(x => x.BpBankAccountId != 0 && i.BankAccounts.All(b => b.Id != x.BpBankAccountId)).ToList()) p.BankAccounts.Remove(gone);

        if (unknown.Count > 0) throw new ValidationException(unknown);
    }

    // ── Roles (FSD §11) ─────────────────────────────────────────────────────────────

    public async Task<PartnerModel> AddRoleAsync(int id, AddRoleRequest request, PartnerCaller caller)
    {
        var partner = await WithEverything(Own()).FirstOrDefaultAsync(p => p.BusinessPartnerId == id) ?? throw new NotFoundException("Partner not found.");
        EnsureEditable(partner);

        var role = request.RoleCode?.Trim() ?? string.Empty;
        if (!PartnerRoles.All.Contains(role))
            throw new ValidationException(messages.Error("roleCode", Msg.OneOf, ("Field", "Role"), ("Allowed", string.Join(", ", PartnerRoles.All))));
        var existingRow = partner.Roles.FirstOrDefault(r => r.RoleCode == role);
        if (existingRow is { IsActive: true })
            throw new ValidationException(messages.Error("roleCode", Msg.AlreadyUsed, ("Field", "role"), ("Code", partner.BpCode), ("Name", partner.LegalName)));

        var today = await clock.TodayAsync(Tenant);
        var errors = new List<ValidationError>();
        // Customer and Bank need an email on the partner (FSD §6 field 12).
        if (role is PartnerRoles.Customer or PartnerRoles.Bank && string.IsNullOrWhiteSpace(partner.Email))
            errors.Add(messages.Error("email", Msg.BpEmailRequiredForCustomer));

        var panel = new PartnerInput { Driver = request.Driver, Vendor = request.Vendor, Customer = request.Customer };
        PartnerRules.Normalize(panel);
        var hasHistoricPanel = role switch
        {
            PartnerRoles.Driver => partner.Driver is not null && panel.Driver is null,
            PartnerRoles.Vendor => partner.Vendor is not null && panel.Vendor is null,
            PartnerRoles.Customer => partner.Customer is not null && panel.Customer is null,
            _ => true
        };
        // A role held before keeps its panel (BR-BP-001); a new one needs its panel filled in.
        if (!hasHistoricPanel) errors.AddRange(Rules.ValidateRolePanel(role, panel, today));
        if (role == PartnerRoles.Driver && panel.Driver?.LicenceNo is { Length: > 0 } licence
            && await duplicates.LicenceHolderAsync(licence, id) is { } holder)
            errors.Add(messages.Error("driver.licenceNo", Msg.AlreadyUsed, ("Field", "licence number"), ("Code", holder.BpCode), ("Name", holder.LegalName)));
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.InTransactionAsync(async ct =>
        {
            var now = DateTime.UtcNow;
            if (existingRow is null) partner.Roles.Add(new BusinessPartnerRole { RoleCode = role, EffectiveFrom = today });
            else { existingRow.IsActive = true; existingRow.EffectiveFrom = today; existingRow.EffectiveTo = null; }   // effective from now; history is not backdated (BR-BP-016)

            ApplyDetails(partner, panel.Driver, panel.Vendor, panel.Customer, [role], caller, existing: partner);
            db.RoleLog.Add(RoleLogRow(partner.BusinessPartnerId, role, "Added", today, request.Reason, caller, now));
            Touch(partner, caller, now);
            await db.SaveChangesAsync(ct);
        });
        return await GetAsync(id);
    }

    public async Task<PartnerModel> RemoveRoleAsync(int id, string roleCode, string? reason, PartnerCaller caller)
    {
        var partner = await WithEverything(Own()).FirstOrDefaultAsync(p => p.BusinessPartnerId == id) ?? throw new NotFoundException("Partner not found.");
        EnsureEditable(partner);

        var row = partner.Roles.FirstOrDefault(r => r.RoleCode == roleCode && r.IsActive) ?? throw new NotFoundException("The partner does not hold that role.");
        if (partner.Roles.Count(r => r.IsActive) <= 1) throw new ValidationException(messages.Error("roleCode", Msg.BpLastRole));   // BR-BP-010

        var blocking = await UsageAsync(id, PartnerIntent.RemoveRole, roleCode);
        if (blocking.Count > 0)
            throw new ValidationException(messages.Error("roleCode", Msg.BpRoleInUse, ("Role", roleCode), ("n", blocking.Count)));   // BR-BP-011

        var today = await clock.TodayAsync(Tenant);
        await db.InTransactionAsync(async ct =>
        {
            var now = DateTime.UtcNow;
            row.IsActive = false;
            row.EffectiveTo = today;   // the panel's row stays, for history (BR-BP-001)
            db.RoleLog.Add(RoleLogRow(partner.BusinessPartnerId, roleCode, "Removed", today, reason, caller, now));
            Touch(partner, caller, now);
            await db.SaveChangesAsync(ct);
        });
        return await GetAsync(id);
    }

    // ── Status (FSD §13.1) ──────────────────────────────────────────────────────────

    public async Task<PartnerModel> ChangeStatusAsync(int id, ChangeStatusRequest request, PartnerCaller caller)
    {
        var partner = await Own().FirstOrDefaultAsync(p => p.BusinessPartnerId == id) ?? throw new NotFoundException("Partner not found.");
        if (partner.Status == PartnerStatuses.Merged) throw new ConflictException("This partner was merged into another and cannot change status.");

        var to = request.Status?.Trim() ?? string.Empty;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var today = await clock.TodayAsync(Tenant);
        var effective = request.EffectiveDate ?? today;

        var errors = new List<ValidationError>();
        if (!PartnerStatuses.Settable.Contains(to)) errors.Add(messages.Error("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", PartnerStatuses.Settable))));
        if (effective > today) errors.Add(messages.Error("effectiveDate", Msg.NotFuture, ("Field", "Effective date")));
        if (to == PartnerStatuses.Blacklisted && (reason is null || reason.Length < 10))
            errors.Add(messages.Error("reason", Msg.MinLength, ("Field", "Reason"), ("Min", 10)));   // a blacklisting must say why
        if (reason is { Length: > 500 }) errors.Add(messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        // Only the moves of §13.1: Active to Inactive or Blacklisted, and back to Active.
        var allowed = (partner.Status, to) switch
        {
            (PartnerStatuses.Active, PartnerStatuses.Inactive) or (PartnerStatuses.Active, PartnerStatuses.Blacklisted) => true,
            (PartnerStatuses.Inactive, PartnerStatuses.Active) or (PartnerStatuses.Blacklisted, PartnerStatuses.Active) => true,
            _ => false
        };
        if (!allowed) throw new ConflictException($"A {partner.Status} partner cannot be set to {to}.");

        if (to == PartnerStatuses.Inactive)
        {
            var blocking = await UsageAsync(id, PartnerIntent.Deactivate, null);
            if (blocking.Count > 0) throw new ValidationException(messages.Error("status", Msg.BpDeactivationBlocked, ("n", blocking.Count)));   // BR-BP-014
        }

        await db.InTransactionAsync(async ct =>
        {
            var now = DateTime.UtcNow;
            db.StatusLog.Add(new BpStatusLog
            {
                BusinessPartnerId = id, FromStatus = partner.Status, ToStatus = to, Reason = reason, EffectiveDate = effective,
                UserId = caller.UserId, UserName = caller.UserName, OccurredOn = now
            });
            partner.Status = to;
            partner.StatusReason = to == PartnerStatuses.Active ? null : reason;
            Touch(partner, caller, now);
            await db.SaveChangesAsync(ct);
        });
        return await GetAsync(id);
    }

    // ── What is in the way ──────────────────────────────────────────────────────────

    public async Task<List<PartnerUsageModel>> UsageAsync(int id, PartnerIntent intent, string? roleCode)
    {
        var found = new List<PartnerUsageModel>();
        foreach (var check in usageChecks)
            found.AddRange((await check.FindBlockingAsync(id, intent, roleCode))
                .Select(u => new PartnerUsageModel { Kind = u.Kind, RecordCode = u.RecordCode, Description = u.Description }));
        return found;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static void EnsureEditable(BusinessPartner p)
    {
        if (p.Status == PartnerStatuses.Merged) throw new ConflictException("This partner was merged into another and is read-only.");
    }

    private static void Touch(BusinessPartner p, PartnerCaller caller, DateTime now)
    {
        p.ModifiedBy = caller.UserId;
        p.ModifiedOn = now;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static BpRoleLog RoleLogRow(int partnerId, string role, string action, DateOnly effective, string? reason, PartnerCaller caller, DateTime now) => new()
    {
        BusinessPartnerId = partnerId, RoleCode = role, Action = action, EffectiveDate = effective, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        UserId = caller.UserId, UserName = caller.UserName, OccurredOn = now
    };

    private static string? ProvinceOf(IReadOnlyDictionary<int, LookupItem> cities, int cityId) =>
        cities.GetValueOrDefault(cityId)?.Attributes.GetValueOrDefault("province");

    private async Task<IReadOnlyDictionary<int, LookupItem>> CitiesAsync(PartnerInput i) =>
        await lookups.FindManyAsync(PlatformLookups.City, i.Addresses.Select(a => a.CityId).Append(i.CityId));

    /// <summary>A city must exist and still be choosable, unless the record already holds it (a retired city on an old record is fine).</summary>
    private async Task<List<ValidationError>> LookupErrorsAsync(PartnerInput i, BusinessPartner? existing)
    {
        var errors = new List<ValidationError>();
        var cities = await CitiesAsync(i);

        void Check(string field, int cityId, bool alreadyHeld)
        {
            if (cityId <= 0 || alreadyHeld) return;
            if (!cities.TryGetValue(cityId, out var city) || !city.IsActive) errors.Add(messages.Error(field, Msg.Invalid, ("Field", "City")));
        }

        Check("cityId", i.CityId, existing is not null && existing.CityId == i.CityId);
        for (var n = 0; n < i.Addresses.Count; n++)
        {
            var a = i.Addresses[n];
            Check($"addresses[{n}].cityId", a.CityId, existing?.Addresses.Any(x => x.BpAddressId == a.Id && x.CityId == a.CityId) == true);
        }

        if (i.BranchId is { } branchId && (existing is null || existing.BranchId != branchId))
        {
            var branch = await branches.FindAsync(branchId);
            if (branch is null || !branch.IsAvailable) errors.Add(messages.Error("branchId", Msg.Invalid, ("Field", "Branch")));
        }
        return errors;
    }

    private async Task<Exception> StaleAsync(int id)
    {
        var last = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == Tenant && a.Entity == nameof(BusinessPartner) && a.RecordId == id.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync();
        var text = messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"), ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC"));
        return new ConflictException(text);
    }

    private async Task<Exception> UniqueViolationAsync(PartnerInput input, int? excludeId, Exception original)
    {
        var errors = await duplicates.UniquenessErrorsAsync(input, excludeId, input is CreatePartnerRequest c ? c.Roles : []);
        return errors.Count > 0 ? new ValidationException(errors) : original;
    }

    // ── Mapping ─────────────────────────────────────────────────────────────────────

    private static PartnerModel ToModel(BusinessPartner p)
    {
        var primary = p.Addresses.FirstOrDefault(a => a.IsPrimary);
        return new PartnerModel
        {
            Id = p.BusinessPartnerId, BpCode = p.BpCode, Status = p.Status, StatusReason = p.StatusReason, Currency = p.Currency,
            PartyType = p.PartyType, LegalName = p.LegalName, DisplayName = p.DisplayName, Cnic = p.Cnic, Ntn = p.Ntn, Strn = p.Strn,
            FilerStatus = p.FilerStatus, PrimaryMobile = p.PrimaryMobile, AlternatePhone = p.AlternatePhone, Email = p.Email,
            CityId = p.CityId, AddressLine = p.AddressLine, BranchId = p.BranchId, Notes = p.Notes,
            OpeningBalance = p.OpeningBalance, OpeningBalanceDate = p.OpeningBalanceDate,
            CreatedOn = p.CreatedOn, ModifiedOn = p.ModifiedOn, RowVersion = Convert.ToBase64String(p.RowVersion),
            Roles = p.Roles.Where(r => r.IsActive).Select(r => r.RoleCode).Order().ToList(),
            RoleHistory = p.Roles.OrderBy(r => r.BpRoleId).Select(r => new PartnerRoleModel { RoleCode = r.RoleCode, IsActive = r.IsActive, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo }).ToList(),
            Driver = p.Driver is null ? null : new DriverModel
            {
                LicenceNo = p.Driver.LicenceNo, LicenceType = p.Driver.LicenceType, LicenceIssueDate = p.Driver.LicenceIssueDate, LicenceExpiryDate = p.Driver.LicenceExpiryDate,
                EmploymentType = p.Driver.EmploymentType, DateOfJoining = p.Driver.DateOfJoining, MonthlyRate = p.Driver.MonthlyRate, CommissionBasis = p.Driver.CommissionBasis,
                CommissionValue = p.Driver.CommissionValue, BloodGroup = p.Driver.BloodGroup, EmergencyContactName = p.Driver.EmergencyContactName,
                EmergencyContactPhone = p.Driver.EmergencyContactPhone, Guarantor = p.Driver.Guarantor, DriverAppAccess = p.Driver.DriverAppAccess
            },
            Vendor = p.Vendor is null ? null : new VendorModel
            {
                SupplyCategories = p.Vendor.SupplyCategories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                PaymentTermDays = p.Vendor.PaymentTermDays, CreditLimit = p.Vendor.CreditLimit
            },
            Customer = p.Customer is null ? null : new CustomerModel
            {
                CustomerType = p.Customer.CustomerType, BillingCycle = p.Customer.BillingCycle, RateBasis = p.Customer.RateBasis,
                DefaultRate = p.Customer.DefaultRate, CreditLimit = p.Customer.CreditLimit, CreditDays = p.Customer.CreditDays
            },
            Contacts = p.Contacts.OrderBy(c => c.BpContactId).Select(c => new ContactModel
            {
                Id = c.BpContactId, ContactName = c.ContactName, Designation = c.Designation, Mobile = c.Mobile, Email = c.Email, IsPrimary = c.IsPrimary, Notes = c.Notes
            }).ToList(),
            // The primary address is the General tab's own fields; the grid holds the others.
            Addresses = p.Addresses.Where(a => !a.IsPrimary).OrderBy(a => a.BpAddressId).Select(a => new AddressModel
            {
                Id = a.BpAddressId, AddressType = a.AddressType, Line1 = a.Line1, Line2 = a.Line2, CityId = a.CityId, ProvinceCode = a.ProvinceCode, Landmark = a.Landmark, IsPrimary = false
            }).ToList(),
            BankAccounts = p.BankAccounts.OrderBy(b => b.BpBankAccountId).Select(b => new BankAccountModel
            {
                Id = b.BpBankAccountId, AccountTitle = b.AccountTitle, BankName = b.BankName, BranchCode = b.BranchCode, AccountNumber = b.AccountNumber, Iban = b.Iban, IsPrimary = b.IsPrimary
            }).ToList()
        };
    }
}

