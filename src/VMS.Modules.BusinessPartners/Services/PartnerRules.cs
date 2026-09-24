using VMS.Modules.BusinessPartners.Domain;
using VMS.Modules.BusinessPartners.Models;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.BusinessPartners.Services;

/// <summary>
/// Every field rule of FSD §6 to §8, checked together so a screen can mark all the problems at once. Nothing here
/// reads the database: that a city exists, that a CNIC is unused, are checked by the service. Field names are the
/// request's own (<c>legalName</c>, <c>contacts[0].mobile</c>) so an error lands on the right control.
/// </summary>
internal sealed class PartnerRules(IMessageCatalogue messages)
{
    private static string Allowed(IEnumerable<string> values) => string.Join(", ", values);

    /// <summary>Trims and tidies what the person typed, so the stored form is one form: mobile with its dash, codes in capitals.</summary>
    public static void Normalize(PartnerInput i)
    {
        i.PartyType = i.PartyType?.Trim() ?? string.Empty;
        i.LegalName = i.LegalName?.Trim() ?? string.Empty;
        i.DisplayName = Blank(i.DisplayName);
        i.Cnic = Blank(i.Cnic);
        i.Ntn = Blank(i.Ntn);
        i.Strn = Blank(i.Strn);
        i.FilerStatus = Blank(i.FilerStatus) ?? FilerStatuses.Unknown;
        i.PrimaryMobile = PartnerFormats.NormalizeMobile(i.PrimaryMobile) ?? i.PrimaryMobile?.Trim() ?? string.Empty;
        i.AlternatePhone = Blank(i.AlternatePhone);
        i.Email = Blank(i.Email);
        i.AddressLine = i.AddressLine?.Trim() ?? string.Empty;
        i.Notes = Blank(i.Notes);

        if (i.Driver is { } d)
        {
            d.LicenceNo = d.LicenceNo?.Trim().ToUpperInvariant() ?? string.Empty;
            d.LicenceType = d.LicenceType?.Trim() ?? string.Empty;
            d.EmploymentType = d.EmploymentType?.Trim() ?? string.Empty;
            d.CommissionBasis = Blank(d.CommissionBasis) ?? RoleFieldValues.CommissionNone;
            d.BloodGroup = Blank(d.BloodGroup)?.ToUpperInvariant();
        }

        foreach (var c in i.Contacts)
        {
            c.ContactName = c.ContactName?.Trim() ?? string.Empty;
            c.Mobile = PartnerFormats.NormalizeMobile(c.Mobile) ?? c.Mobile?.Trim() ?? string.Empty;
            c.Email = Blank(c.Email);
            c.Designation = Blank(c.Designation);
            c.Notes = Blank(c.Notes);
        }
        foreach (var a in i.Addresses)
        {
            a.AddressType = a.AddressType?.Trim() ?? string.Empty;
            a.Line1 = a.Line1?.Trim() ?? string.Empty;
            a.Line2 = Blank(a.Line2);
            a.Landmark = Blank(a.Landmark);
        }
        foreach (var b in i.BankAccounts)
        {
            b.AccountTitle = b.AccountTitle?.Trim() ?? string.Empty;
            b.BankName = b.BankName?.Trim() ?? string.Empty;
            b.BranchCode = Blank(b.BranchCode);
            b.AccountNumber = b.AccountNumber?.Trim() ?? string.Empty;
            b.Iban = Blank(b.Iban)?.Replace(" ", string.Empty).ToUpperInvariant();
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <param name="activeRoles">The roles the partner will hold after this save.</param>
    /// <param name="creating">True for a new partner: a licence must expire in the future, and the opening balance is checked.</param>
    /// <param name="today">The company's calendar day, for "not in the future".</param>
    public List<ValidationError> Validate(PartnerInput i, IReadOnlyCollection<string> activeRoles, bool creating, DateOnly today)
    {
        var errors = new List<ValidationError>();
        void Add(string? field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        void Required(string field, string label) => Add(field, Msg.Required, ("Field", label));
        void TooLong(string field, string label, int max) => Add(field, Msg.MaxLength, ("Field", label), ("Max", max));
        void OneOf(string field, string label, IEnumerable<string> allowed) => Add(field, Msg.OneOf, ("Field", label), ("Allowed", Allowed(allowed)));

        // ── General (§6) ────────────────────────────────────────────────────────────
        if (!PartyTypes.All.Contains(i.PartyType)) OneOf("partyType", "Party type", PartyTypes.All);

        if (i.LegalName.Length == 0) Required("legalName", "Legal name");
        else if (i.LegalName.Length < 3) Add("legalName", Msg.MinLength, ("Field", "Legal name"), ("Min", 3));
        else if (i.LegalName.Length > 150) TooLong("legalName", "Legal name", 150);

        if (i.DisplayName is { Length: > 60 }) TooLong("displayName", "Display name", 60);

        if (i.PartyType == PartyTypes.Person && i.Cnic is null) Required("cnic", "CNIC");
        if (i.Cnic is not null && !PartnerFormats.IsCnic(i.Cnic)) Add("cnic", Msg.BpCnicFormat);

        if (i.PartyType == PartyTypes.Company && i.Ntn is null) Required("ntn", "NTN");
        if (i.Ntn is not null && !PartnerFormats.IsNtn(i.Ntn)) Add("ntn", Msg.Format, ("Field", "NTN"));

        if (i.Strn is { Length: > 20 }) TooLong("strn", "STRN", 20);
        if (i.FilerStatus is not null && !FilerStatuses.All.Contains(i.FilerStatus)) OneOf("filerStatus", "Filer status", FilerStatuses.All);

        if (i.PrimaryMobile.Length == 0) Required("primaryMobile", "Primary mobile");
        else if (PartnerFormats.NormalizeMobile(i.PrimaryMobile) is null) Add("primaryMobile", Msg.BpMobileFormat);

        if (i.AlternatePhone is not null && !PartnerFormats.IsPhone(i.AlternatePhone)) Add("alternatePhone", Msg.Format, ("Field", "Alternate phone"));

        if (i.Email is not null && !PartnerFormats.IsEmail(i.Email)) Add("email", Msg.Email);
        if (i.Email is null && (activeRoles.Contains(PartnerRoles.Customer) || activeRoles.Contains(PartnerRoles.Bank)))
            Add("email", Msg.BpEmailRequiredForCustomer);

        if (i.CityId <= 0) Required("cityId", "City");

        if (i.AddressLine.Length == 0) Required("addressLine", "Address");
        else if (i.AddressLine.Length > 250) TooLong("addressLine", "Address", 250);

        if (i.Notes is { Length: > 1000 }) TooLong("notes", "Notes", 1000);

        if (creating && (i.OpeningBalance ?? 0m) != 0m)
        {
            if (i.OpeningBalanceDate is null) Add("openingBalanceDate", Msg.BpOpeningBalanceDateRequired);
            else if (i.OpeningBalanceDate > today) Add("openingBalanceDate", Msg.BpOpeningBalanceDateFuture);
        }
        if (creating && i.OpeningBalance is { } balance && Math.Abs(balance) >= 10_000_000_000_000m)
            Add("openingBalance", Msg.Max, ("Field", "Opening balance"), ("Max", "9,999,999,999,999.99"));

        // ── Role panels (§7) ────────────────────────────────────────────────────────
        if (activeRoles.Contains(PartnerRoles.Driver)) ValidateDriver(i.Driver, creating, today, errors);
        if (activeRoles.Contains(PartnerRoles.Vendor)) ValidateVendor(i.Vendor, errors);
        if (activeRoles.Contains(PartnerRoles.Customer)) ValidateCustomer(i.Customer, errors);

        // ── Child collections (§8) ──────────────────────────────────────────────────
        ValidateContacts(i.Contacts, errors);
        ValidateAddresses(i.Addresses, errors);
        ValidateBankAccounts(i.BankAccounts, errors);

        return errors;
    }

    /// <summary>The checks for a role panel on its own: used when a role is added to an existing partner.</summary>
    public List<ValidationError> ValidateRolePanel(string roleCode, PartnerInput panel, DateOnly today)
    {
        var errors = new List<ValidationError>();
        switch (roleCode)
        {
            case PartnerRoles.Driver: ValidateDriver(panel.Driver, creating: true, today, errors); break;
            case PartnerRoles.Vendor: ValidateVendor(panel.Vendor, errors); break;
            case PartnerRoles.Customer: ValidateCustomer(panel.Customer, errors); break;
        }
        return errors;
    }

    private void ValidateDriver(DriverModel? d, bool creating, DateOnly today, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        void Required(string field, string label) => Add(field, Msg.Required, ("Field", label));

        if (d is null)
        {
            Required("driver", "Driver details");
            return;
        }

        if (d.LicenceNo.Length == 0) Add("driver.licenceNo", Msg.BpLicenceNumberRequired);
        else if (d.LicenceNo.Length > 30) Add("driver.licenceNo", Msg.MaxLength, ("Field", "Licence number"), ("Max", 30));

        if (!RoleFieldValues.LicenceTypes.Contains(d.LicenceType))
            Add("driver.licenceType", Msg.OneOf, ("Field", "Licence type"), ("Allowed", Allowed(RoleFieldValues.LicenceTypes)));

        if (d.LicenceExpiryDate is null) Required("driver.licenceExpiryDate", "Licence expiry date");
        else if (creating && d.LicenceExpiryDate <= today) Add("driver.licenceExpiryDate", Msg.BpLicenceExpired);

        if (d.LicenceIssueDate > today) Add("driver.licenceIssueDate", Msg.NotFuture, ("Field", "Licence issue date"));

        if (!RoleFieldValues.EmploymentTypes.Contains(d.EmploymentType))
            Add("driver.employmentType", Msg.OneOf, ("Field", "Employment type"), ("Allowed", Allowed(RoleFieldValues.EmploymentTypes)));

        if (d.EmploymentType == RoleFieldValues.Employee && d.DateOfJoining is null) Required("driver.dateOfJoining", "Date of joining");
        if (d.DateOfJoining > today) Add("driver.dateOfJoining", Msg.NotFuture, ("Field", "Date of joining"));

        if (d.MonthlyRate < 0) Add("driver.monthlyRate", Msg.Min, ("Field", "Monthly rate"), ("Min", 0));

        var basis = d.CommissionBasis ?? RoleFieldValues.CommissionNone;
        if (!RoleFieldValues.CommissionBases.Contains(basis))
            Add("driver.commissionBasis", Msg.OneOf, ("Field", "Commission basis"), ("Allowed", Allowed(RoleFieldValues.CommissionBases)));
        else if (basis != RoleFieldValues.CommissionNone)
        {
            if (d.CommissionValue is null) Required("driver.commissionValue", "Commission value");
            else if (d.CommissionValue < 0) Add("driver.commissionValue", Msg.Min, ("Field", "Commission value"), ("Min", 0));
            else if (basis == RoleFieldValues.CommissionPercent && d.CommissionValue > 100) Add("driver.commissionValue", Msg.Max, ("Field", "Commission value"), ("Max", 100));
        }

        if (d.EmergencyContactPhone is not null && !PartnerFormats.IsPhone(d.EmergencyContactPhone))
            Add("driver.emergencyContactPhone", Msg.Format, ("Field", "Emergency contact phone"));
    }

    private void ValidateVendor(VendorModel? v, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        if (v is null)
        {
            Add("vendor", Msg.Required, ("Field", "Vendor details"));
            return;
        }

        if (v.SupplyCategories.Count == 0) Add("vendor.supplyCategories", Msg.Required, ("Field", "Supply categories"));
        else if (v.SupplyCategories.Any(c => !RoleFieldValues.SupplyCategories.Contains(c)))
            Add("vendor.supplyCategories", Msg.OneOf, ("Field", "Supply categories"), ("Allowed", Allowed(RoleFieldValues.SupplyCategories)));

        if (v.PaymentTermDays < 0) Add("vendor.paymentTermDays", Msg.Min, ("Field", "Payment terms"), ("Min", 0));
        if (v.CreditLimit < 0) Add("vendor.creditLimit", Msg.Min, ("Field", "Credit limit"), ("Min", 0));
    }

    private void ValidateCustomer(CustomerModel? c, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        if (c is null)
        {
            Add("customer", Msg.Required, ("Field", "Customer details"));
            return;
        }

        if (!RoleFieldValues.CustomerTypes.Contains(c.CustomerType))
            Add("customer.customerType", Msg.OneOf, ("Field", "Customer type"), ("Allowed", Allowed(RoleFieldValues.CustomerTypes)));
        if (!RoleFieldValues.BillingCycles.Contains(c.BillingCycle))
            Add("customer.billingCycle", Msg.OneOf, ("Field", "Billing cycle"), ("Allowed", Allowed(RoleFieldValues.BillingCycles)));
        if (c.RateBasis is not null && !RoleFieldValues.RateBases.Contains(c.RateBasis))
            Add("customer.rateBasis", Msg.OneOf, ("Field", "Rate basis"), ("Allowed", Allowed(RoleFieldValues.RateBases)));
        if (c.DefaultRate < 0) Add("customer.defaultRate", Msg.Min, ("Field", "Default rate"), ("Min", 0));
        if (c.CreditLimit < 0) Add("customer.creditLimit", Msg.Min, ("Field", "Credit limit"), ("Min", 0));
        if (c.CreditDays < 0) Add("customer.creditDays", Msg.Min, ("Field", "Credit days"), ("Min", 0));
    }

    private void ValidateContacts(List<ContactModel> contacts, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        for (var n = 0; n < contacts.Count; n++)
        {
            var c = contacts[n];
            var at = $"contacts[{n}]";
            if (c.ContactName.Length == 0) Add($"{at}.contactName", Msg.Required, ("Field", "Contact name"));
            else if (c.ContactName.Length > 100) Add($"{at}.contactName", Msg.MaxLength, ("Field", "Contact name"), ("Max", 100));
            if (c.Designation is { Length: > 60 }) Add($"{at}.designation", Msg.MaxLength, ("Field", "Designation"), ("Max", 60));
            if (c.Mobile.Length == 0) Add($"{at}.mobile", Msg.Required, ("Field", "Mobile"));
            else if (PartnerFormats.NormalizeMobile(c.Mobile) is null) Add($"{at}.mobile", Msg.BpMobileFormat);
            if (c.Email is not null && !PartnerFormats.IsEmail(c.Email)) Add($"{at}.email", Msg.Email);
            if (c.Notes is { Length: > 250 }) Add($"{at}.notes", Msg.MaxLength, ("Field", "Notes"), ("Max", 250));
        }
        if (contacts.Count(c => c.IsPrimary) > 1) Add("contacts", Msg.OnlyOnePrimary, ("Field", "contact"));
    }

    private void ValidateAddresses(List<AddressModel> addresses, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        for (var n = 0; n < addresses.Count; n++)
        {
            var a = addresses[n];
            var at = $"addresses[{n}]";
            if (!AddressTypes.All.Contains(a.AddressType)) Add($"{at}.addressType", Msg.OneOf, ("Field", "Address type"), ("Allowed", Allowed(AddressTypes.All)));
            if (a.Line1.Length == 0) Add($"{at}.line1", Msg.Required, ("Field", "Address line 1"));
            else if (a.Line1.Length > 250) Add($"{at}.line1", Msg.MaxLength, ("Field", "Address line 1"), ("Max", 250));
            if (a.Line2 is { Length: > 250 }) Add($"{at}.line2", Msg.MaxLength, ("Field", "Address line 2"), ("Max", 250));
            if (a.CityId <= 0) Add($"{at}.cityId", Msg.Required, ("Field", "City"));
            if (a.Landmark is { Length: > 150 }) Add($"{at}.landmark", Msg.MaxLength, ("Field", "Landmark"), ("Max", 150));
        }
        if (addresses.Count(a => a.IsPrimary) > 1) Add("addresses", Msg.OnlyOnePrimary, ("Field", "address"));
    }

    private void ValidateBankAccounts(List<BankAccountModel> accounts, List<ValidationError> errors)
    {
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        for (var n = 0; n < accounts.Count; n++)
        {
            var b = accounts[n];
            var at = $"bankAccounts[{n}]";
            if (b.AccountTitle.Length == 0) Add($"{at}.accountTitle", Msg.Required, ("Field", "Account title"));
            else if (b.AccountTitle.Length > 150) Add($"{at}.accountTitle", Msg.MaxLength, ("Field", "Account title"), ("Max", 150));
            if (b.BankName.Length == 0) Add($"{at}.bankName", Msg.Required, ("Field", "Bank name"));
            else if (b.BankName.Length > 100) Add($"{at}.bankName", Msg.MaxLength, ("Field", "Bank name"), ("Max", 100));
            if (b.BranchCode is { Length: > 80 }) Add($"{at}.branchCode", Msg.MaxLength, ("Field", "Branch / code"), ("Max", 80));
            if (b.AccountNumber.Length == 0) Add($"{at}.accountNumber", Msg.Required, ("Field", "Account number"));
            else if (!PartnerFormats.IsAccountNumber(b.AccountNumber)) Add($"{at}.accountNumber", Msg.Format, ("Field", "Account number"));
            if (b.Iban is not null && !PartnerFormats.IsIban(b.Iban)) Add($"{at}.iban", Msg.Format, ("Field", "IBAN"));
        }
        if (accounts.Count(b => b.IsPrimary) > 1) Add("bankAccounts", Msg.OnlyOnePrimary, ("Field", "bank account"));

        var duplicate = accounts.Select((b, n) => (b, n)).GroupBy(x => (x.b.BankName.ToLowerInvariant(), x.b.AccountNumber)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) Add($"bankAccounts[{duplicate.Last().n}].accountNumber", Msg.ListedTwice, ("Field", "account number"));
    }
}
