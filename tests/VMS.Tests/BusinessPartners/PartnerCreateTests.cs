using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>S1-BP-01…05, 07, 08: creating a partner, and everything that must stop one being created.</summary>
[Collection(ApiCollection.Name)]
public sealed class PartnerCreateTests(ApiFactory factory)
{
    // ── Creating ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_partner_is_saved_with_the_next_code_active_and_holding_its_role()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var first = await w.CreateAsync(w.Person());
        var second = await w.CreateAsync(w.Person());

        Assert.Matches(@"^BP-\d{2}-00001$", first["bpCode"]!.GetValue<string>());
        Assert.Matches(@"^BP-\d{2}-00002$", second["bpCode"]!.GetValue<string>());
        Assert.Equal("Active", first["status"]!.GetValue<string>());
        Assert.Equal(["Workshop"], first["roles"]!.AsArray().Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public async Task Numbers_are_counted_for_each_tenant_on_its_own()
    {
        var a = await PartnerWorld.CreateAsync(factory);
        var b = await PartnerWorld.CreateAsync(factory);

        await a.CreateAsync(a.Person());
        var firstOfB = await b.CreateAsync(b.Person());

        Assert.EndsWith("00001", firstOfB["bpCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_general_tab_address_is_kept_as_the_primary_registered_address_and_the_start_of_each_role_is_logged()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var created = await w.CreateAsync(w.Person());
        var id = created["id"]!.GetValue<int>();

        var addresses = factory.Query("SELECT AddressType, Line1, IsPrimary, ProvinceCode FROM bp.BpAddresses WHERE BusinessPartnerId = @i", ("@i", id));
        var address = Assert.Single(addresses);
        Assert.Equal("Registered", address["AddressType"]);
        Assert.Equal("12 Main Boulevard, Gulberg", address["Line1"]);
        Assert.True((bool)address["IsPrimary"]!);
        Assert.NotNull(address["ProvinceCode"]);   // filled in from the city, not sent by the screen

        var log = Assert.Single(factory.Query("SELECT RoleCode, Action, UserId, UserName FROM bp.BpRoleLog WHERE BusinessPartnerId = @i", ("@i", id)));
        Assert.Equal(("Workshop", "Added", 1, "Admin User"), (log["RoleCode"], log["Action"], log["UserId"], log["UserName"]));

        Assert.Empty(created["addresses"]!.AsArray());   // the primary one is the General tab's own fields
    }

    [Fact]
    public async Task The_created_partner_is_written_to_the_audit_trail_by_the_person_who_made_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var id = (await w.CreateAsync(w.Person()))["id"]!.GetValue<int>();

        var rows = factory.Query(
            "SELECT UserId, UserName, Field, NewValue, RequiredPermission FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'BusinessPartner' AND RecordId = @r AND Action = 'Created'",
            ("@t", w.Tenant), ("@r", id.ToString()));

        var snapshot = Assert.Single(rows, r => string.IsNullOrEmpty((string?)r["Field"]));
        Assert.Equal((1, "Admin User"), (snapshot["UserId"], snapshot["UserName"]));
        Assert.Contains("BP-26-", (string)snapshot["NewValue"]!);
        Assert.DoesNotContain("OpeningBalance", (string)snapshot["NewValue"]!);   // a restricted value is never in the snapshot everyone can read...
        var opening = Assert.Single(rows, r => (string?)r["Field"] == "OpeningBalance");
        Assert.Equal(PermissionCodes.BP_FIELD_OPENING_VIEW, opening["RequiredPermission"]);   // ...but in its own row, marked with the permission to see it
    }

    [Fact]
    public async Task A_company_with_a_vendor_panel_child_rows_and_a_bank_account_is_saved_whole()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Company(roles: ["Vendor", "Customer"]);
        body["customer"] = new JsonObject { ["customerType"] = "Factory", ["billingCycle"] = "Monthly", ["creditLimit"] = 500000, ["creditDays"] = 30 };
        body["vendor"]!["paymentTermDays"] = 15;
        body["contacts"] = new JsonArray(
            new JsonObject { ["contactName"] = "Accounts Officer", ["mobile"] = "03001234599", ["isPrimary"] = true },
            new JsonObject { ["contactName"] = "Store Keeper", ["mobile"] = "0301-7654321" });
        body["addresses"] = new JsonArray(new JsonObject { ["addressType"] = "Billing", ["line1"] = "Head Office", ["cityId"] = w.OtherCity });
        body["bankAccounts"] = new JsonArray(new JsonObject
        {
            ["accountTitle"] = "Ali Traders", ["bankName"] = "Standard Chartered", ["accountNumber"] = "0123456789", ["iban"] = "pk36 scbl 0000 0011 2345 6702", ["isPrimary"] = true
        });

        var created = await w.CreateAsync(body);

        Assert.Equal(2, created["contacts"]!.AsArray().Count);
        Assert.Equal("0300-1234599", created["contacts"]![0]!["mobile"]!.GetValue<string>());   // the missing dash is added
        Assert.Equal("Billing", created["addresses"]![0]!["addressType"]!.GetValue<string>());
        Assert.Equal("PK36SCBL0000001123456702", created["bankAccounts"]![0]!["iban"]!.GetValue<string>());   // stored without spaces, in capitals
        Assert.Equal("Factory", created["customer"]!["customerType"]!.GetValue<string>());
        Assert.Equal(15, created["vendor"]!["paymentTermDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_mobile_is_stored_with_its_dash()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body["primaryMobile"] = "0311" + Random.Shared.Next(1000000, 9999999);

        var created = await w.CreateAsync(body);

        Assert.Matches(@"^0311-\d{7}$", created["primaryMobile"]!.GetValue<string>());
    }

    // ── Validation ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_problem_is_reported_at_once_each_on_its_own_field()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var response = await w.PostAsync(new JsonObject());
        var errors = await PartnerWorld.ErrorsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var field in new[] { "partyType", "legalName", "primaryMobile", "cityId", "addressLine", "roles" })
            Assert.Contains(errors, e => e.Field == field);
        Assert.Contains(errors, e => e.Field == "roles" && e.Code == Msg.BpRoleRequired);
    }

    [Fact]
    public async Task A_person_needs_a_cnic_and_a_company_needs_an_ntn()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var person = w.Person(); person.Remove("cnic");
        var company = w.Company(); company.Remove("ntn");

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(person), "cnic", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(company), "ntn", Msg.Required);
    }

    [Theory]
    [InlineData("cnic", "3520212345671", Msg.BpCnicFormat)]
    [InlineData("primaryMobile", "0400-1234567", Msg.BpMobileFormat)]
    [InlineData("email", "not an email", Msg.Email)]
    [InlineData("alternatePhone", "x", Msg.Format)]
    [InlineData("legalName", "Al", Msg.MinLength)]
    public async Task A_field_in_the_wrong_format_is_refused(string field, string value, string code)
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body[field] = value;

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(body), field, code);
    }

    [Fact]
    public async Task A_role_that_does_not_exist_and_a_city_that_does_not_exist_are_refused()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var role = w.Person(role: "Pirate");
        var city = w.Person(); city["cityId"] = 999_999;

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(role), "roles", Msg.OneOf);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(city), "cityId", Msg.Invalid);
    }

    [Fact]
    public async Task A_city_of_another_tenant_is_not_a_city()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        var body = mine.Person();
        body["cityId"] = other.City;   // the other tenant's row for the same city

        // Lookups are seeded for each tenant, so the same city has a different id in each.
        if (other.City != mine.City)
            await PartnerWorld.AssertRefusedAsync(await mine.PostAsync(body), "cityId", Msg.Invalid);
    }

    [Fact]
    public async Task A_customer_or_a_bank_needs_an_email()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var customer = w.Company(roles: "Customer");
        customer["customer"] = new JsonObject { ["customerType"] = "Factory", ["billingCycle"] = "Monthly" };
        customer.Remove("vendor");
        customer.Remove("email");

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(customer), "email", Msg.BpEmailRequiredForCustomer);
    }

    [Fact]
    public async Task Only_one_contact_bank_account_or_address_can_be_primary_and_an_account_cannot_be_listed_twice()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body["contacts"] = new JsonArray(
            new JsonObject { ["contactName"] = "A", ["mobile"] = "0300-1111111", ["isPrimary"] = true },
            new JsonObject { ["contactName"] = "B", ["mobile"] = "0300-2222222", ["isPrimary"] = true });
        var account = () => new JsonObject { ["accountTitle"] = "T", ["bankName"] = "HBL", ["accountNumber"] = "12345" };
        body["bankAccounts"] = new JsonArray(account(), account());
        body["addresses"] = new JsonArray(new JsonObject { ["addressType"] = "Billing", ["line1"] = "x", ["cityId"] = w.City, ["isPrimary"] = true });

        var errors = await PartnerWorld.ErrorsAsync(await w.PostAsync(body));

        Assert.Contains(errors, e => e is { Field: "contacts", Code: Msg.OnlyOnePrimary });
        Assert.Contains(errors, e => e is { Field: "bankAccounts[1].accountNumber", Code: Msg.ListedTwice });
        Assert.Contains(errors, e => e is { Field: "addresses", Code: Msg.OnlyOnePrimary });
    }

    [Fact]
    public async Task A_child_row_with_a_bad_value_names_the_row_and_the_field()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var body = w.Person();
        body["contacts"] = new JsonArray(new JsonObject { ["contactName"] = "A", ["mobile"] = "0300-1111111" }, new JsonObject { ["contactName"] = "", ["mobile"] = "12" });
        body["bankAccounts"] = new JsonArray(new JsonObject { ["accountTitle"] = "T", ["bankName"] = "HBL", ["accountNumber"] = "12345", ["iban"] = "PK37SCBL0000001123456702" });

        var errors = await PartnerWorld.ErrorsAsync(await w.PostAsync(body));

        Assert.Contains(errors, e => e is { Field: "contacts[1].contactName", Code: Msg.Required });
        Assert.Contains(errors, e => e is { Field: "contacts[1].mobile", Code: Msg.BpMobileFormat });
        Assert.Contains(errors, e => e is { Field: "bankAccounts[0].iban", Code: Msg.Format });
        Assert.DoesNotContain(errors, e => e.Field.StartsWith("contacts[0]"));
    }

    [Fact]
    public async Task A_refused_save_leaves_nothing_behind_and_uses_no_number()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var bad = w.Person(); bad["addressLine"] = "";
        await w.PostAsync(bad);
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BusinessPartners WHERE TenantId = @t", ("@t", w.Tenant)));

        var first = await w.CreateAsync(w.Person());

        Assert.EndsWith("00001", first["bpCode"]!.GetValue<string>());   // the refused attempt did not use up a number
    }

    // ── Roles' own panels ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_driver_needs_licence_details_and_a_licence_that_has_not_expired()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var none = w.Person(role: "Driver");
        var expired = w.Person(role: "Driver"); expired["driver"] = PartnerWorld.Driver(expiry: "2020-01-01");
        var incomplete = w.Person(role: "Driver"); incomplete["driver"] = new JsonObject { ["employmentType"] = "Employee", ["licenceType"] = "Car" };

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(none), "driver", Msg.Required);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(expired), "driver.licenceExpiryDate", Msg.BpLicenceExpired);
        var errors = await PartnerWorld.ErrorsAsync(await w.PostAsync(incomplete));
        foreach (var field in new[] { "driver.licenceNo", "driver.licenceType", "driver.licenceExpiryDate", "driver.dateOfJoining" })
            Assert.Contains(errors, e => e.Field == field);
    }

    [Fact]
    public async Task A_percentage_commission_cannot_exceed_a_hundred_and_a_vendor_needs_a_supply_category()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var driver = w.Person(role: "Driver"); driver["driver"] = PartnerWorld.Driver(); driver["driver"]!["commissionValue"] = 150;
        var vendor = w.Company(); vendor["vendor"] = new JsonObject { ["supplyCategories"] = new JsonArray() };

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(driver), "driver.commissionValue", Msg.Max);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(vendor), "vendor.supplyCategories", Msg.Required);
    }

    [Fact]
    public async Task A_licence_number_belongs_to_one_active_driver()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var first = w.Person(role: "Driver"); first["driver"] = PartnerWorld.Driver("LHR-778899");
        var holder = await w.CreateAsync(first);

        var second = w.Person(role: "Driver"); second["driver"] = PartnerWorld.Driver("lhr-778899");   // typed in lower case
        var response = await w.PostAsync(second);

        await PartnerWorld.AssertRefusedAsync(response, "driver.licenceNo", Msg.AlreadyUsed);
        var message = (await PartnerWorld.ErrorsAsync(response)).Single(e => e.Field == "driver.licenceNo").Message;
        Assert.Contains(holder["bpCode"]!.GetValue<string>(), message);   // says who holds it
    }

    // ── Money the caller may not see ────────────────────────────────────────────────

    [Fact]
    public async Task Salary_and_credit_figures_are_left_out_for_someone_without_the_permission()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var driver = w.Person(role: "Driver"); driver["driver"] = PartnerWorld.Driver();
        var vendor = w.Company(roles: "Vendor"); vendor["vendor"]!["creditLimit"] = 250000;
        var driverId = (await w.CreateAsync(driver))["id"]!.GetValue<int>();
        var vendorId = (await w.CreateAsync(vendor))["id"]!.GetValue<int>();
        var reader = w.As("Reader", 2, PermissionCodes.BP_VIEW);

        var seenDriver = await w.GetAsync(driverId, reader);
        var seenVendor = await w.GetAsync(vendorId, reader);
        var fullDriver = await w.GetAsync(driverId);

        Assert.Equal(60000m, fullDriver["driver"]!["monthlyRate"]!.GetValue<decimal>());
        Assert.NotNull(seenDriver["driver"]!["licenceNo"]);
        foreach (var key in new[] { "monthlyRate", "commissionBasis", "commissionValue" })
            Assert.False(seenDriver["driver"]!.AsObject().ContainsKey(key), key);
        Assert.False(seenVendor["vendor"]!.AsObject().ContainsKey("creditLimit"));
        Assert.False(seenVendor.ContainsKey("openingBalance"));
    }

    [Fact]
    public async Task A_person_who_cannot_see_the_salary_cannot_set_it_either()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var clerk = w.As("Clerk", 3, PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE);
        var body = w.Person(role: "Driver"); body["driver"] = PartnerWorld.Driver();

        var created = await w.CreateAsync(body, clerk);

        var stored = factory.Scalar<decimal?>("SELECT MonthlyRate FROM bp.BpDriverDetails WHERE BusinessPartnerId = @i", ("@i", created["id"]!.GetValue<int>()));
        Assert.Null(stored);   // the 60,000 sent was ignored
    }

    [Fact]
    public async Task An_opening_balance_needs_a_date_that_is_not_in_the_future_and_the_permission_to_set_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);

        var noDate = w.Person(); noDate["openingBalance"] = 1500;
        var future = w.Person(); future["openingBalance"] = 1500; future["openingBalanceDate"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(noDate), "openingBalanceDate", Msg.BpOpeningBalanceDateRequired);
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(future), "openingBalanceDate", Msg.BpOpeningBalanceDateFuture);

        var ok = w.Person(); ok["openingBalance"] = 1500; ok["openingBalanceDate"] = "2026-01-01";
        var created = await w.CreateAsync(ok);
        Assert.Equal(1500, created["openingBalance"]!.GetValue<decimal>());

        var clerk = w.As("Clerk", 3, PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE);
        var sneaky = w.Person(); sneaky["openingBalance"] = 9999; sneaky["openingBalanceDate"] = "2026-01-01";
        var made = await w.CreateAsync(sneaky, clerk);
        Assert.Equal(0m, factory.Scalar<decimal>("SELECT OpeningBalance FROM bp.BusinessPartners WHERE BusinessPartnerId = @i", ("@i", made["id"]!.GetValue<int>())));
    }

    // ── Uniqueness (hard duplicates) ────────────────────────────────────────────────

    [Fact]
    public async Task A_cnic_already_on_file_is_refused_and_names_who_has_it()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person());
        var again = w.Person(); again["cnic"] = existing["cnic"]!.DeepClone();

        var response = await w.PostAsync(again);

        await PartnerWorld.AssertRefusedAsync(response, "cnic", Msg.BpCnicUsed);
        var message = (await PartnerWorld.ErrorsAsync(response)).Single(e => e.Field == "cnic").Message;
        Assert.Contains(existing["bpCode"]!.GetValue<string>(), message);
        Assert.Contains(existing["legalName"]!.GetValue<string>(), message);
    }

    [Fact]
    public async Task A_companys_ntn_is_unique_among_companies_and_a_persons_may_repeat_a_companys()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Company());
        var again = w.Company(); again["ntn"] = existing["ntn"]!.DeepClone();

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(again), "ntn", Msg.BpNtnUsed);

        // A person who is a sole trader may quote the NTN their company already has.
        var person = w.Person(); person["ntn"] = existing["ntn"]!.DeepClone();
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(person)).StatusCode);
    }

    [Fact]
    public async Task An_strn_is_unique_too()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var first = w.Person(); first["strn"] = "1700000012345";
        await w.CreateAsync(first);
        var second = w.Person(); second["strn"] = "1700000012345";

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(second), "strn", Msg.AlreadyUsed);
    }

    [Fact]
    public async Task The_same_cnic_in_another_tenant_is_nobodys_business()
    {
        var a = await PartnerWorld.CreateAsync(factory);
        var b = await PartnerWorld.CreateAsync(factory);
        var mine = await a.CreateAsync(a.Person());
        var theirs = b.Person(); theirs["cnic"] = mine["cnic"]!.DeepClone();

        Assert.Equal(HttpStatusCode.Created, (await b.PostAsync(theirs)).StatusCode);
    }

    [Fact]
    public async Task Two_people_saving_the_same_cnic_at_the_same_moment_get_one_partner_and_a_refusal()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var cnic = PartnerWorld.NextCnic();
        var bodies = Enumerable.Range(0, 6).Select(_ => { var b = w.Person(); b["cnic"] = cnic; return b; }).ToList();

        var results = await Task.WhenAll(bodies.Select(b => w.PostAsync(b)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BusinessPartners WHERE TenantId = @t AND Cnic = @c", ("@t", w.Tenant), ("@c", cnic)));
        // No numbers were lost to the failed attempts: the codes run 1, 2, … without a gap.
        var next = await w.CreateAsync(w.Person());
        Assert.EndsWith("00002", next["bpCode"]!.GetValue<string>());
    }

    // ── Possible duplicates (soft) ──────────────────────────────────────────────────

    [Fact]
    public async Task The_same_name_in_the_same_city_is_a_warning_the_user_has_to_answer()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person("Karim Bakhsh Transport"));

        var response = await w.PostAsync(w.Person("Karim Bakhsh Transport"));

        await PartnerWorld.AssertRefusedAsync(response, "legalName", Msg.BpSimilarName);
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM bp.BusinessPartners WHERE TenantId = @t", ("@t", w.Tenant)));

        var answered = w.Person("Karim Bakhsh Transport");
        answered["acknowledgedDuplicateIds"] = new JsonArray(existing["id"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(answered)).StatusCode);
    }

    [Fact]
    public async Task A_name_that_is_nearly_the_same_in_the_same_city_is_a_warning_but_in_another_city_it_is_not()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        await w.CreateAsync(w.Person("Muhammad Ali Traders"));

        var sameCity = w.Person("Muhamad Ali Traders");
        var elsewhere = w.Person("Muhamad Ali Traders"); elsewhere["cityId"] = w.OtherCity;

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(sameCity), "legalName", Msg.BpSimilarName);
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(elsewhere)).StatusCode);
    }

    [Fact]
    public async Task A_mobile_number_another_partner_has_is_a_warning_and_the_override_is_recorded_with_who_and_when()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person());
        var shared = existing["primaryMobile"]!.GetValue<string>();

        var body = w.Person(); body["primaryMobile"] = shared.Replace("-", "");   // typed without the dash: still the same number
        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(body), "primaryMobile", Msg.OthersHaveThis);

        body["acknowledgedDuplicateIds"] = new JsonArray(existing["id"]!.GetValue<int>());
        var editor = w.As("Shabana Khan", 42, PartnerWorld.Everything);
        var created = await w.CreateAsync(body, editor);

        var note = Assert.Single(factory.Query(
            "SELECT UserId, UserName, OccurredAt, NewValue FROM core.AuditEntries WHERE TenantId = @t AND Entity = 'BusinessPartner' AND RecordId = @r AND Action = 'DuplicateOverridden'",
            ("@t", w.Tenant), ("@r", created["id"]!.GetValue<int>().ToString())));
        Assert.Equal(42, note["UserId"]);
        Assert.Equal("Shabana Khan", note["UserName"]);
        Assert.Contains(existing["bpCode"]!.GetValue<string>(), (string)note["NewValue"]!);
    }

    [Fact]
    public async Task Naming_a_partner_that_is_not_a_duplicate_does_not_excuse_the_ones_that_are()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person("Noor Fatima Goods"));
        var unrelated = await w.CreateAsync(w.Person());

        var body = w.Person("Noor Fatima Goods");
        body["acknowledgedDuplicateIds"] = new JsonArray(unrelated["id"]!.GetValue<int>());

        await PartnerWorld.AssertRefusedAsync(await w.PostAsync(body), "legalName", Msg.BpSimilarName);
        Assert.NotNull(existing);
    }

    [Fact]
    public async Task A_cnic_held_by_a_merged_partner_is_free_again_and_the_database_agrees()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var old = await w.CreateAsync(w.Person());
        factory.Execute("UPDATE bp.BusinessPartners SET Status = 'Merged' WHERE BusinessPartnerId = @i", ("@i", old["id"]!.GetValue<int>()));
        var again = w.Person(); again["cnic"] = old["cnic"]!.DeepClone();

        // BR-BP-013: a merged partner no longer holds its CNIC, in the check and in the unique index alike.
        Assert.Equal(HttpStatusCode.Created, (await w.PostAsync(again)).StatusCode);
    }

    [Fact]
    public async Task The_duplicate_check_the_screen_makes_while_typing_finds_the_same_partners_the_save_would()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var existing = await w.CreateAsync(w.Person("Rehmat Ullah Brothers"));

        var response = await w.Admin.PostAsJsonAsync("/api/partners/duplicate-check", new
        {
            partyType = "Person",
            legalName = "Rehmat Ullah Brothers",
            cnic = existing["cnic"]!.GetValue<string>(),
            primaryMobile = existing["primaryMobile"]!.GetValue<string>(),
            cityId = w.City
        });
        var matches = (await response.DataAsync()).GetProperty("matches").EnumerateArray().ToList();

        var match = Assert.Single(matches);   // one partner, reported for its strongest reason
        Assert.Equal("Cnic", match.GetProperty("matchType").GetString());
        Assert.True(match.GetProperty("isHard").GetBoolean());
        Assert.Equal(existing["bpCode"]!.GetValue<string>(), match.GetProperty("bpCode").GetString());
        Assert.Equal(["Workshop"], match.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    // ── Who may ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_needs_the_create_permission_and_viewing_needs_the_view_permission()
    {
        var w = await PartnerWorld.CreateAsync(factory);
        var viewer = w.As("Viewer", 5, PermissionCodes.BP_VIEW);
        var creator = w.As("Creator", 6, PermissionCodes.BP_CREATE);
        var created = await w.CreateAsync(w.Person());
        var id = created["id"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/partners", w.Person())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync($"/api/partners/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creator.GetAsync("/api/partners")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/partners/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/partners/{id}")).StatusCode);
    }

    [Fact]
    public async Task Someone_elses_partner_is_not_found_rather_than_forbidden()
    {
        var mine = await PartnerWorld.CreateAsync(factory);
        var other = await PartnerWorld.CreateAsync(factory);
        var theirs = await other.CreateAsync(other.Person());

        var response = await mine.Admin.GetAsync($"/api/partners/{theirs["id"]!.GetValue<int>()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
