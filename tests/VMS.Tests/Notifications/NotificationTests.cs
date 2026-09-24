using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Notifications;

/// <summary>
/// S7-NOT-01 to S7-NOT-06: the notification rule master, the evaluator's lead-time and escalation logic across every
/// event type, the save-time "immediately" hooks (FR-BP-016, FR-VH-008), and the in-app list.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory)
{
    private static string Day(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>
    /// A real, DB-backed user holding the Tenant Admin role (every permission), inserted directly rather than through
    /// the sign-up flow — the Notifications module's recipient lookup resolves against the real Auth tables, and a
    /// <see cref="TestTokens"/> caller (a signature-only stand-in used everywhere else in this suite) is not a row
    /// there. Tenant Admin holds every permission (<c>AuthDataSeeder</c>), so this one user is a valid recipient for
    /// every rule this test file exercises, whichever permission each rule names.
    /// </summary>
    private int RegisterRecipientAsync(VehicleWorld w, string emailPrefix)
    {
        var roleId = factory.Scalar<int>("SELECT RoleID FROM auth.Roles WHERE RoleCode = 'TENANT_ADMIN' AND IsGlobal = 1");
        var email = $"{emailPrefix}.{Guid.NewGuid():N}@test.local";
        factory.Execute(
            "INSERT INTO auth.UserAccounts (TenantId, FirstName, LastName, Email, PasswordHash, RoleID, IsActive, IsDeleted, CreatedBy, CreatedDate) " +
            "VALUES (@t, @fn, @ln, @e, @ph, @r, 1, 0, 0, @cd)",
            ("@t", w.Tenant), ("@fn", "Notify"), ("@ln", "Recipient"), ("@e", email), ("@ph", "x"), ("@r", roleId), ("@cd", DateTime.UtcNow));
        var userId = factory.Scalar<int>("SELECT UserID FROM auth.UserAccounts WHERE Email = @e", ("@e", email));
        factory.Execute("INSERT INTO auth.UserRoles (UserID, RoleID, ScopeType, TenantId) VALUES (@u, @r, 'AllBranches', @t)", ("@u", userId), ("@r", roleId), ("@t", w.Tenant));
        return userId;
    }

    /// <summary>Asserts success, not just fire-and-forget: a repeat run that hits an already-written notification must come back clean (the dedup index's own catch, BR-VH-030's pattern), not as a failed request.</summary>
    private static async Task RunJobAsync(VehicleWorld w)
    {
        var response = await w.Admin.PostAsJsonAsync("/api/admin/jobs/notifications/run", new { });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private int NotificationCount(Guid tenant, int userId, string eventType, bool? isEscalation = null)
    {
        var sql = "SELECT COUNT(*) FROM notif.Notifications WHERE TenantId = @t AND UserId = @u AND EventType = @e" + (isEscalation is null ? "" : " AND IsEscalation = @esc");
        var parameters = new List<(string, object)> { ("@t", tenant), ("@u", userId), ("@e", eventType) };
        if (isEscalation is { } esc) parameters.Add(("@esc", esc));
        return factory.Scalar<int>(sql, parameters.ToArray());
    }

    // ── S7-NOT-01: the rule master seeds lazily and matches the FSD ──────────────────

    [Fact]
    public async Task A_new_tenant_gets_the_six_seeded_rules_matching_the_FSD()
    {
        var w = await VehicleWorld.CreateAsync(factory);

        var rules = (await (await w.Admin.GetAsync("/api/admin/notification-rules")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();

        Assert.Equal(6, rules.Count);
        var documentExpiry = rules.Single(r => r["eventType"]!.GetValue<string>() == "DocumentExpiry");
        Assert.Equal(30, documentExpiry["leadDays"]!.GetValue<int>());
        Assert.True(documentExpiry["isActive"]!.GetValue<bool>());

        var chargeDue = rules.Single(r => r["eventType"]!.GetValue<string>() == "ChargeDue");
        Assert.Equal(3, chargeDue["leadDays"]!.GetValue<int>());

        var overdue = rules.Single(r => r["eventType"]!.GetValue<string>() == "ChargeOverdue");
        Assert.Equal(1, overdue["leadDays"]!.GetValue<int>());
        Assert.Equal(3, overdue["escalationAfterDays"]!.GetValue<int>());
        Assert.Equal(PermissionCodes.ADM_NOTIFICATION_MANAGE, overdue["escalationPermission"]!.GetValue<string>());

        var ending = rules.Single(r => r["eventType"]!.GetValue<string>() == "ChargeEnding");
        Assert.Equal(30, ending["leadDays"]!.GetValue<int>());
        Assert.Null(ending["escalationAfterDays"]);

        var installment = rules.Single(r => r["eventType"]!.GetValue<string>() == "InstallmentDue");
        Assert.Equal(3, installment["leadDays"]!.GetValue<int>());

        var warranty = rules.Single(r => r["eventType"]!.GetValue<string>() == "ItemWarrantyEnd");
        Assert.Equal(15, warranty["leadDays"]!.GetValue<int>());
    }

    [Fact]
    public async Task An_admin_can_edit_a_rules_lead_days_and_recipient_but_the_event_type_and_name_never_change()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var rules = (await (await w.Admin.GetAsync("/api/admin/notification-rules")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        var documentExpiry = rules.Single(r => r["eventType"]!.GetValue<string>() == "DocumentExpiry");
        var id = documentExpiry["id"]!.GetValue<int>();

        var negative = await w.Admin.PutAsJsonAsync($"/api/admin/notification-rules/{id}", new { leadDays = -1, recipientPermission = PermissionCodes.DOC_REGISTER_VIEW, isActive = true });
        await PartnerWorld.AssertRefusedAsync(negative, "leadDays", Msg.Invalid);

        var missingRecipient = await w.Admin.PutAsJsonAsync($"/api/admin/notification-rules/{id}", new { leadDays = 10, isActive = true });
        await PartnerWorld.AssertRefusedAsync(missingRecipient, "recipientPermission", Msg.Required);

        var saved = await w.Admin.PutAsJsonAsync($"/api/admin/notification-rules/{id}", new { leadDays = 45, recipientPermission = PermissionCodes.VEH_VIEW, isActive = false });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var updated = PartnerWorld.AsObject(await saved.DataAsync());
        Assert.Equal(45, updated["leadDays"]!.GetValue<int>());
        Assert.Equal(PermissionCodes.VEH_VIEW, updated["recipientPermission"]!.GetValue<string>());
        Assert.False(updated["isActive"]!.GetValue<bool>());
        Assert.Equal("DocumentExpiry", updated["eventType"]!.GetValue<string>());   // fixed, BR-NOT-001

        var overdueRules = rules.Single(r => r["eventType"]!.GetValue<string>() == "ChargeOverdue");
        var missingEscalation = await w.Admin.PutAsJsonAsync($"/api/admin/notification-rules/{overdueRules["id"]!.GetValue<int>()}", new { leadDays = 1, recipientPermission = PermissionCodes.FIN_DUE_CONFIRM, isActive = true });
        await PartnerWorld.AssertRefusedAsync(missingEscalation, "escalationAfterDays", Msg.Invalid);
    }

    [Fact]
    public async Task Only_someone_with_notification_manage_can_reach_the_rule_admin_screen()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var noAccess = w.As("Fleet Manager", 601, PermissionCodes.VEH_VIEW);

        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/admin/notification-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await w.Factory.CreateClient().GetAsync("/api/admin/notification-rules")).StatusCode);
    }

    // ── S7-NOT-02, DocumentExpiry: uploading notifies immediately, not at the next run ──

    [Fact]
    public async Task Uploading_a_document_expiring_within_the_lead_window_notifies_immediately()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "docnear");
        var vehicle = await w.ActiveAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "INSURANCE_POLICY").GetProperty("id").GetInt32();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" },
            { new StringContent(Day(20)), "expiryDate" },   // inside the default 30-day lead
            { new StringContent("POL-1"), "documentNumber" },
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "doc.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents", form);
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());

        // No job run at all: the save-time hook (S7-NOT-02) already evaluated this one document.
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "DocumentExpiry"));
    }

    [Fact]
    public async Task Uploading_a_document_expiring_well_outside_the_lead_window_notifies_nobody()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "docfar");
        var vehicle = await w.ActiveAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "INSURANCE_POLICY").GetProperty("id").GetInt32();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" },
            { new StringContent(Day(200)), "expiryDate" },   // far outside the 30-day lead
            { new StringContent("POL-2"), "documentNumber" },
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "doc.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents", form);
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        await RunJobAsync(w);   // even the hourly run finds nothing due yet

        Assert.Equal(0, NotificationCount(w.Tenant, recipient, "DocumentExpiry"));
    }

    [Fact]
    public async Task Running_the_job_twice_never_notifies_the_same_document_twice()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "docdedup");
        var vehicle = await w.ActiveAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "FITNESS_CERTIFICATE").GetProperty("id").GetInt32();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" },
            { new StringContent(Day(10)), "expiryDate" },
            { new StringContent("FIT-1"), "documentNumber" },   // FITNESS_CERTIFICATE requires one
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "doc.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents", form);   // the upload itself already notifies once
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());

        await RunJobAsync(w);
        await RunJobAsync(w);

        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "DocumentExpiry"));
    }

    // ── S7-NOT-03, ChargeDue / ChargeOverdue / escalation ────────────────────────────

    private static async Task<int> ChargeTypeAsync(VehicleWorld w, string description) =>
        (await (await w.Admin.GetAsync("/api/lookups/RECURRING_CHARGE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == description).GetProperty("id").GetInt32();

    private static async Task<int> ExpenseTypeAsync(VehicleWorld w, string description) =>
        (await (await w.Admin.GetAsync("/api/lookups/EXPENSE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == description).GetProperty("id").GetInt32();

    private static Task<HttpResponseMessage> RunChargesJobAsync(VehicleWorld w) => w.Admin.PostAsJsonAsync("/api/admin/jobs/recurring-charges/run", new { });

    [Fact]
    public async Task A_due_charge_entry_notifies_and_becoming_overdue_notifies_again_as_a_separate_event()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "chargedue");
        var vehicle = await w.ActiveAsync(acquired: Day(-30));
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/recurring-charges", new
        {
            chargeTypeId = chargeType, payeeId = trackerCo, expenseTypeId = expenseType, amount = 5_000, amountBasis = "Fixed",
            frequency = "Monthly", dueDay = DateOnly.FromDateTime(DateTime.UtcNow).Day, startDate = Day(-1), postingMode = "GenerateAsDue", generateLeadDays = 7,
        });

        await RunChargesJobAsync(w);   // generates the Due entry
        await RunJobAsync(w);
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeDue"));
        Assert.Equal(0, NotificationCount(w.Tenant, recipient, "ChargeOverdue"));

        // Back-date it into overdue territory (BR-VH's own job flips Due -> Overdue by date), then evaluate again.
        var chargeId = factory.Scalar<int>("SELECT VehicleRecurringChargeId FROM veh.VehicleRecurringChargeEntries WHERE VehicleId = @v", ("@v", VehicleWorld.Id(vehicle)));
        factory.Execute("UPDATE veh.VehicleRecurringCharges SET NextDueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", DateOnly.Parse(Day(-5))), ("@id", chargeId));
        factory.Execute("UPDATE veh.VehicleRecurringChargeEntries SET DueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", DateOnly.Parse(Day(-5))), ("@id", chargeId));
        await RunChargesJobAsync(w);   // flips the entry's status to Overdue
        await RunJobAsync(w);

        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeDue"));                             // unchanged: the Due notice already sent stays sent
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: false));    // a distinct event, notified once on its own
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: true));     // 5 days overdue already exceeds the 3-day escalation threshold, so escalation fires on this same evaluation

        // Running the job again the same day repeats neither.
        await RunJobAsync(w);
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: false));
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: true));
    }

    [Fact]
    public async Task An_overdue_charge_escalates_once_the_configured_days_have_passed_even_to_the_same_recipient()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "escalate");   // Tenant Admin: holds both FIN_DUE_CONFIRM and ADM_NOTIFICATION_MANAGE
        var vehicle = await w.ActiveAsync(acquired: Day(-30));
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");
        var created = PartnerWorld.AsObject(await (await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/recurring-charges", new
        {
            chargeTypeId = chargeType, payeeId = trackerCo, expenseTypeId = expenseType, amount = 5_000, amountBasis = "Fixed",
            frequency = "Monthly", dueDay = DateOnly.FromDateTime(DateTime.UtcNow).Day, startDate = Day(-20), postingMode = "GenerateAsDue", generateLeadDays = 7,
        })).DataAsync());

        // Two days overdue: under the 3-day escalation threshold. One ordinary notice, no escalation yet.
        factory.Execute("UPDATE veh.VehicleRecurringCharges SET NextDueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", DateOnly.Parse(Day(-2))), ("@id", created["id"]!.GetValue<int>()));
        await RunChargesJobAsync(w);
        await RunJobAsync(w);
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: false));
        Assert.Equal(0, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: true));

        // Now five days overdue: past the threshold. The escalation notice lands alongside the ordinary one already sent —
        // additional, not instead of, even though the escalation recipient is the very same real person here.
        factory.Execute("UPDATE veh.VehicleRecurringChargeEntries SET DueDate = @d WHERE VehicleRecurringChargeId = @id", ("@d", DateOnly.Parse(Day(-5))), ("@id", created["id"]!.GetValue<int>()));
        await RunJobAsync(w);
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: false));   // no repeat yet: not 3 days since the ordinary notice itself was sent
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: true));

        // Running it again immediately duplicates neither.
        await RunJobAsync(w);
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: false));
        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeOverdue", isEscalation: true));
    }

    // ── S7-NOT-02, ChargeEnding: configuring an end date notifies immediately ────────

    [Fact]
    public async Task Configuring_a_charge_with_an_end_date_inside_the_lead_window_notifies_immediately()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "chargeend");
        var vehicle = await w.ActiveAsync(acquired: Day(-30));
        var trackerCo = await w.PartnerAsync("TrackerCompany");
        var chargeType = await ChargeTypeAsync(w, "Tracker Fee");
        var expenseType = await ExpenseTypeAsync(w, "Other");

        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/recurring-charges", new
        {
            chargeTypeId = chargeType, payeeId = trackerCo, expenseTypeId = expenseType, amount = 5_000, amountBasis = "Fixed",
            frequency = "Monthly", dueDay = DateOnly.FromDateTime(DateTime.UtcNow).Day, startDate = Day(-10), endDate = Day(20), postingMode = "GenerateAsDue", generateLeadDays = 7,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ChargeEnding"));   // no job run: the create call's own hook fired
    }

    // ── S7-NOT-02, InstallmentDue: activating with a finance schedule notifies immediately ──

    [Fact]
    public async Task Activating_a_bank_leased_vehicle_notifies_about_its_first_installment_immediately()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "installment");
        var vehicle = await w.CreateAsync(w.Truck());
        var bank = await w.PartnerAsync("Bank");
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/acquisition")).DataAsync());
        await w.Admin.PutAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/acquisition", new
        {
            acquisitionDate = Day(-10), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer", rowVersion = block["rowVersion"]!.GetValue<string>()
        });
        var financeType = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();
        await w.Admin.PutAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/finance", new
        {
            financeTypeId = financeType, bankId = bank, agreementNo = "AGR-" + Guid.NewGuid().ToString("N")[..8], agreementDate = Day(-9), financeAmount = 3_000_000, downPayment = 2_000_000,
            installmentAmount = 3_000_000, frequency = "Monthly", tenure = 1, firstDueDate = Day(2),   // inside the 3-day default lead
        });
        await w.UploadRegistrationBookAsync(VehicleWorld.Id(vehicle));
        var fresh = await w.GetAsync(VehicleWorld.Id(vehicle));

        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/activate", new { category = "BankLeased", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());

        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "InstallmentDue"));   // the activation call's own hook fired, no job run
    }

    // ── S7-NOT-03, ItemWarrantyEnd ────────────────────────────────────────────────────

    [Fact]
    public async Task An_attached_items_warranty_ending_soon_is_picked_up_by_the_job()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "warranty");
        var vehicle = await w.ActiveAsync();
        factory.Execute(
            "INSERT INTO veh.VehicleAttachedItems (TenantId, VehicleId, ItemTypeId, Description, InstallationDate, WarrantyUntil, Status, CreatedBy, CreatedOn) " +
            "VALUES (@t, @v, @it, @d, @i, @w, 'Attached', 1, @c)",
            ("@t", w.Tenant), ("@v", VehicleWorld.Id(vehicle)), ("@it", w.ItemType), ("@d", "Container"), ("@i", DateOnly.Parse(Day(-100))), ("@w", DateOnly.Parse(Day(5))), ("@c", DateTime.UtcNow));

        await RunJobAsync(w);   // no save-time hook for a row inserted directly; the hourly job still finds it

        Assert.Equal(1, NotificationCount(w.Tenant, recipient, "ItemWarrantyEnd"));
    }

    // ── S7-NOT-06: the in-app list ────────────────────────────────────────────────────

    [Fact]
    public async Task A_recipient_sees_their_own_notifications_and_marking_one_read_updates_the_unread_count()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var recipient = RegisterRecipientAsync(w, "inbox");
        var vehicle = await w.ActiveAsync();
        var typeId = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == "INSURANCE_POLICY").GetProperty("id").GetInt32();
        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "documentTypeId" }, { new StringContent(Day(5)), "expiryDate" }, { new StringContent("POL-3"), "documentNumber" },
        };
        var part = new ByteArrayContent(VehicleWorld.Pdf());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "doc.pdf");
        var uploaded = await w.Admin.PostAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents", form);
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());

        var recipientClient = w.As("Recipient", recipient, PermissionCodes.VEH_VIEW);   // any signed-in caller may read their own inbox
        var unread = PartnerWorld.AsObject(await (await recipientClient.GetAsync("/api/notifications/unread-count")).DataAsync());
        Assert.Equal(1, unread["count"]!.GetValue<int>());

        var list = (await (await recipientClient.GetAsync("/api/notifications")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        var row = Assert.Single(list);
        Assert.Equal("DocumentExpiry", row["eventType"]!.GetValue<string>());
        Assert.False(row["isRead"]!.GetValue<bool>());

        var marked = await recipientClient.PostAsJsonAsync($"/api/notifications/{row["id"]!.GetValue<int>()}/read", new { });
        Assert.True(marked.IsSuccessStatusCode, await marked.Content.ReadAsStringAsync());
        var unreadAfter = PartnerWorld.AsObject(await (await recipientClient.GetAsync("/api/notifications/unread-count")).DataAsync());
        Assert.Equal(0, unreadAfter["count"]!.GetValue<int>());

        // Someone else's inbox is empty and their own read-mark does nothing to another user's row.
        var otherClient = w.As("Someone Else", 602, PermissionCodes.VEH_VIEW);
        Assert.Empty((await (await otherClient.GetAsync("/api/notifications")).DataAsync()).EnumerateArray());
        var stealMark = await otherClient.PostAsJsonAsync($"/api/notifications/{row["id"]!.GetValue<int>()}/read", new { });
        Assert.Equal(HttpStatusCode.NotFound, stealMark.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_reach_the_notification_endpoints()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var anonymous = w.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications/unread-count")).StatusCode);

        var noAdmin = w.As("Fleet Manager", 603, PermissionCodes.VEH_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAdmin.PostAsJsonAsync("/api/admin/jobs/notifications/run", new { })).StatusCode);
    }
}
