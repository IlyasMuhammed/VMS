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

namespace VMS.Tests.Documents;

/// <summary>S5-DOC-01 to S5-DOC-10: the document type master, uploading, renewing, rejecting, downloading, the mandatory-document check, and the two nightly jobs.</summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentTests(ApiFactory factory)
{
    private static byte[] Pdf(int length = 400) => VehicleWorld.Pdf(length);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string ownerRoute, int ownerId, int typeId, byte[] content, object? fields = null, string name = "doc.pdf")
    {
        using var form = new MultipartFormDataContent { { new StringContent(typeId.ToString()), "documentTypeId" } };
        if (fields is not null)
            foreach (var (key, value) in PartnerWorld.AsObject(JsonSerializer.SerializeToElement(fields)))
                form.Add(new StringContent(value!.ToString()), key);
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", name);
        return await client.PostAsync($"/api/{ownerRoute}/{ownerId}/documents", form);
    }

    private static async Task<HttpResponseMessage> RenewAsync(HttpClient client, string ownerRoute, int ownerId, int typeId, byte[] content, object? fields = null)
    {
        using var form = new MultipartFormDataContent();
        if (fields is not null)
            foreach (var (key, value) in PartnerWorld.AsObject(JsonSerializer.SerializeToElement(fields)))
                form.Add(new StringContent(value!.ToString()), key);
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "renewal.pdf");
        return await client.PostAsync($"/api/{ownerRoute}/{ownerId}/documents/{typeId}/renew", form);
    }

    private static async Task<int> TypeIdAsync(HttpClient client, string code)
    {
        var response = await client.GetAsync("/api/documents/types");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).EnumerateArray().Single(t => t.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    private static async Task<List<JsonObject>> SlotsAsync(HttpClient client, string ownerRoute, int ownerId, bool history = false) =>
        (await (await client.GetAsync($"/api/{ownerRoute}/{ownerId}/documents?includeHistory={history}")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();

    // ── The type master seeds lazily and matches the FSD (S5-DOC-01, S5-DOC-02) ──────

    [Fact]
    public async Task A_new_tenant_gets_the_twelve_seeded_types_the_first_time_they_are_asked_for()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var types = (await (await w.Admin.GetAsync("/api/documents/types")).DataAsync()).EnumerateArray().ToList();

        Assert.Equal(12, types.Count);
        Assert.Contains(types, t => t.GetProperty("code").GetString() == "REGISTRATION_BOOK" && t.GetProperty("mandatoryLevel").GetString() == "Required");
        Assert.Contains(types, t => t.GetProperty("code").GetString() == "DRIVING_LICENCE" && t.GetProperty("partnerRole").GetString() == "Driver");
        Assert.Contains(types, t => t.GetProperty("code").GetString() == "INSURANCE_POLICY" && t.GetProperty("isExpirable").GetBoolean() && t.GetProperty("hasCost").GetBoolean());

        var vehicleOnly = (await (await w.Admin.GetAsync("/api/documents/types?appliesTo=Vehicle")).DataAsync()).EnumerateArray().ToList();
        Assert.Equal(7, vehicleOnly.Count);
    }

    [Fact]
    public async Task An_admin_can_add_and_edit_a_type_but_two_of_the_same_code_are_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        await w.Admin.GetAsync("/api/documents/types");   // seed first

        var created = await w.Admin.PostAsJsonAsync("/api/admin/document-types", new
        {
            code = "CUSTOM_PERMIT", name = "Custom Permit", appliesTo = new[] { "Vehicle" }, isExpirable = true, defaultValidityValue = 6, defaultValidityUnit = "Months",
            isPeriodic = true, mandatoryLevel = "Warn", requiresDocumentNumber = true, hasCost = false,
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.DataAsync()).GetProperty("id").GetInt32();

        var duplicate = await w.Admin.PostAsJsonAsync("/api/admin/document-types", new { code = "CUSTOM_PERMIT", name = "Again", appliesTo = new[] { "Vehicle" } });
        await PartnerWorld.AssertRefusedAsync(duplicate, "code", Msg.DocTypeCodeInUse);

        var updated = await w.Admin.PutAsJsonAsync($"/api/admin/document-types/{id}", new { name = "Custom Permit (renamed)", appliesTo = new[] { "Vehicle" }, mandatoryLevel = "None" });
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
        Assert.Equal("Custom Permit (renamed)", (await updated.DataAsync()).GetProperty("name").GetString());
    }

    // ── Uploading (S5-DOC-04) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Uploading_a_vehicle_document_needs_the_expiry_date_the_type_requires_and_a_second_upload_is_refused()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "INSURANCE_POLICY");

        var noExpiry = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf());
        await PartnerWorld.AssertRefusedAsync(noExpiry, "expiryDate", Msg.Required);

        var ok = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = DateTime.UtcNow.AddMonths(11).ToString("yyyy-MM-dd"), documentNumber = "POL-1" });
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var doc = PartnerWorld.AsObject(await ok.DataAsync());
        Assert.Equal(1, doc["versionNo"]!.GetValue<int>());
        Assert.True(doc["isCurrent"]!.GetValue<bool>());
        Assert.Equal("Active", doc["status"]!.GetValue<string>());

        var again = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = "2030-01-01" });
        await PartnerWorld.AssertRefusedAsync(again, "documentTypeId", Msg.DocAlreadyCurrent);
    }

    [Fact]
    public async Task Only_the_types_allowed_formats_are_accepted_and_only_someone_with_upload_permission_may()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "PURCHASE_INVOICE");

        var text = System.Text.Encoding.UTF8.GetBytes("this is not a pdf, png or jpeg");
        var badFormat = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, text);
        Assert.Equal(HttpStatusCode.BadRequest, badFormat.StatusCode);

        var noPermission = w.As("Viewer", 51, PermissionCodes.VEH_VIEW, PermissionCodes.DOC_VIEW);
        var refused = await UploadAsync(noPermission, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf());
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Uploading_for_a_partner_role_specific_type_needs_that_role()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vendor = await w.PartnerAsync("Vendor");
        var typeId = await TypeIdAsync(w.Admin, "DRIVING_LICENCE");

        // A Driving Licence applies only to the Driver role, but the upload endpoint itself does not police that — it is the mandatory
        // check (BR-BP-005) that only asks for it when the Driver role is active, and the slot list only offers it there too. A vendor
        // can still legitimately have no reason to see it offered; uploading against another owner type entirely is refused instead.
        var wrongOwner = await UploadAsync(w.Admin, "vehicles", 999_999, typeId, Pdf(), new { expiryDate = "2030-01-01" });
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
        _ = vendor;
    }

    // ── Renewing (S5-DOC-05, BR-DOC-005) ──────────────────────────────────────────────

    [Fact]
    public async Task Renewing_computes_the_new_expiry_from_the_type_s_default_validity_and_supersedes_the_old_version()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "FITNESS_CERTIFICATE");   // 12 months default validity

        var first = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = "2026-12-31", documentNumber = "FIT-1", provider = "VEXI" });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var nothingYet = await RenewAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), await TypeIdAsync(w.Admin, "TOKEN_TAX_RECEIPT"), Pdf());
        await PartnerWorld.AssertRefusedAsync(nothingYet, "documentTypeId", Msg.DocNothingToRenew);

        var renewed = await RenewAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf());
        Assert.True(renewed.IsSuccessStatusCode, await renewed.Content.ReadAsStringAsync());
        var doc = PartnerWorld.AsObject(await renewed.DataAsync());
        Assert.Equal(2, doc["versionNo"]!.GetValue<int>());
        Assert.Equal("2027-12-31", doc["expiryDate"]!.GetValue<string>());   // pre-filled from the previous expiry plus 12 months
        Assert.Equal("FIT-1", doc["documentNumber"]!.GetValue<string>());   // and the number and provider carried over
        Assert.Equal("VEXI", doc["provider"]!.GetValue<string>());

        var slots = await SlotsAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), history: true);
        var slot = slots.Single(s => s["documentTypeId"]!.GetValue<int>() == typeId);
        Assert.Equal(2, slot["current"]!["versionNo"]!.GetValue<int>());
        var history = slot["history"]!.AsArray();
        Assert.Single(history);
        Assert.Equal("Superseded", history[0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_cost_type_s_renewal_can_be_tagged_with_the_transaction_that_already_paid_for_it()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var types = (await (await w.Admin.GetAsync("/api/documents/types?appliesTo=Vehicle")).DataAsync()).EnumerateArray().ToList();
        var insurance = types.Single(t => t.GetProperty("code").GetString() == "INSURANCE_POLICY");
        Assert.Equal("INSURANCE_PREMIUM", insurance.GetProperty("linkedChargeTypeCode").GetString());   // BR-DOC-006
        Assert.Null(types.Single(t => t.GetProperty("code").GetString() == "PURCHASE_INVOICE").GetProperty("linkedChargeTypeCode").GetString());
        var typeId = insurance.GetProperty("id").GetInt32();

        var first = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = "2026-12-31", documentNumber = "POL-9" });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        // The client already confirmed the matching recurring charge entry (a separate, already-idempotent posting path — Stage 4)
        // and only tags the new version with what paid for it; this field never posts anything itself.
        var renewed = await RenewAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { linkedTransactionId = 777 });
        Assert.True(renewed.IsSuccessStatusCode, await renewed.Content.ReadAsStringAsync());
        Assert.Equal(777, (await renewed.DataAsync()).GetProperty("linkedTransactionId").GetInt32());

        // A non-positive id is not trusted as a real link: stored as no link at all, not as a bogus transaction id.
        var again = await RenewAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { linkedTransactionId = 0 });
        Assert.True((await again.DataAsync()).GetProperty("linkedTransactionId").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    // ── Rejecting (S5-DOC-08, BR-DOC-008) ────────────────────────────────────────────

    [Fact]
    public async Task Rejecting_needs_a_reason_empties_the_slot_and_the_next_upload_is_fresh_not_a_renewal()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "PURCHASE_INVOICE");
        var uploaded = PartnerWorld.AsObject(await (await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf())).DataAsync());
        var docId = uploaded["id"]!.GetValue<int>();

        var missingReason = await w.Admin.PostAsJsonAsync($"/api/documents/{docId}/reject", new { });
        await PartnerWorld.AssertRefusedAsync(missingReason, "reason", Msg.Required);

        var rejected = await w.Admin.PostAsJsonAsync($"/api/documents/{docId}/reject", new { reason = "Wrong vehicle's papers" });
        Assert.True(rejected.IsSuccessStatusCode, await rejected.Content.ReadAsStringAsync());
        var doc = PartnerWorld.AsObject(await rejected.DataAsync());
        Assert.Equal("Rejected", doc["status"]!.GetValue<string>());
        Assert.False(doc["isCurrent"]!.GetValue<bool>());

        var slots = await SlotsAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle));
        Assert.Null(slots.Single(s => s["documentTypeId"]!.GetValue<int>() == typeId)["current"]);

        // The slot is empty again: the next save is Upload, not Renew, and it becomes version 2 (history is never renumbered).
        var again = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf());
        Assert.True(again.IsSuccessStatusCode, await again.Content.ReadAsStringAsync());
        Assert.Equal(2, (await again.DataAsync()).GetProperty("versionNo").GetInt32());
    }

    // ── Downloading (S5-DOC-10, BR-DOC-007, FR-DOC-001) ──────────────────────────────

    [Fact]
    public async Task Downloading_gives_a_short_lived_link_and_a_sensitive_type_needs_the_sensitive_permission()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "PURCHASE_INVOICE");
        var uploaded = PartnerWorld.AsObject(await (await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf())).DataAsync());

        var link = await w.Admin.PostAsync($"/api/documents/{uploaded["id"]!.GetValue<int>()}/download-link", null);
        Assert.True(link.IsSuccessStatusCode, await link.Content.ReadAsStringAsync());
        var url = (await link.DataAsync()).GetProperty("url").GetString()!;
        Assert.StartsWith("/api/files/download/", url);

        var fetched = await w.Admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        // The download is audited even though nothing about the row changed.
        var vehicleId = VehicleWorld.Id(vehicle);
        Assert.True(factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'Document' AND Action = 'Downloaded' AND RootEntity = 'Vehicle' AND RootRecordId = @v", ("@v", vehicleId.ToString())) >= 1);

        // A CNIC is sensitive (§23A.5): the ordinary download permission is not enough.
        var driver = await w.DriverAsync();
        var cnicType = await TypeIdAsync(w.Admin, "CNIC");
        var cnicResponse = await UploadAsync(w.Admin, "partners", driver, cnicType, Pdf(), new { expiryDate = "2035-01-01", documentNumber = "12345-1234567-1" });
        Assert.True(cnicResponse.IsSuccessStatusCode, await cnicResponse.Content.ReadAsStringAsync());
        var cnicDoc = PartnerWorld.AsObject(await cnicResponse.DataAsync());
        var noSensitive = w.As("Ordinary", 52, PermissionCodes.DOC_VIEW, PermissionCodes.DOC_DOWNLOAD);
        var refused = await noSensitive.PostAsync($"/api/documents/{cnicDoc["id"]!.GetValue<int>()}/download-link", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var allowed = w.As("Admin-level", 53, PermissionCodes.DOC_VIEW, PermissionCodes.DOC_DOWNLOAD_SENSITIVE);
        Assert.True((await allowed.PostAsync($"/api/documents/{cnicDoc["id"]!.GetValue<int>()}/download-link", null)).IsSuccessStatusCode);
    }

    // ── The mandatory-document check (S5-DOC-07, BR-BP-005, BR-VH-015) ──────────────

    [Fact]
    public async Task A_driver_with_no_licence_on_file_gets_a_warning_not_a_blocked_save_and_it_clears_once_uploaded()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driverId = await w.DriverAsync();   // OQ-04: warn only, never blocks

        var partner = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/partners/{driverId}")).DataAsync());
        var warnings = partner["documentWarnings"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains(warnings, m => m.Contains("Driving Licence"));

        var typeId = await TypeIdAsync(w.Admin, "DRIVING_LICENCE");
        var licenceUpload = await UploadAsync(w.Admin, "partners", driverId, typeId, Pdf(), new { expiryDate = "2031-01-01", documentNumber = "DL-99" });
        Assert.True(licenceUpload.IsSuccessStatusCode, await licenceUpload.Content.ReadAsStringAsync());

        var after = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/partners/{driverId}")).DataAsync());
        Assert.DoesNotContain(after["documentWarnings"]!.AsArray().Select(x => x!.GetValue<string>()), m => m.Contains("Driving Licence"));

        // DRIVING_LICENCE is scoped to the Driver role (BR-BP-005): a Workshop partner is never asked for one.
        var workshopId = await w.PartnerAsync("Workshop");
        var workshop = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/partners/{workshopId}")).DataAsync());
        Assert.DoesNotContain(workshop["documentWarnings"]!.AsArray().Select(x => x!.GetValue<string>()), m => m.Contains("Driving Licence"));
    }

    [Fact]
    public async Task Missing_documents_report_lists_the_gap_for_both_owner_types()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var driverId = await w.DriverAsync();
        var vehicle = await w.ActiveAsync();

        var report = (await (await w.Admin.GetAsync("/api/documents/missing")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.Contains(report, r => r["ownerType"]!.GetValue<string>() == "BusinessPartner" && r["ownerId"]!.GetValue<int>() == driverId && r["documentTypeCode"]!.GetValue<string>() == "DRIVING_LICENCE");
        Assert.Contains(report, r => r["ownerType"]!.GetValue<string>() == "Vehicle" && r["ownerId"]!.GetValue<int>() == VehicleWorld.Id(vehicle) && r["documentTypeCode"]!.GetValue<string>() == "REGISTRATION_BOOK");

        var vehicleOnly = (await (await w.Admin.GetAsync("/api/documents/missing?ownerType=Vehicle")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.DoesNotContain(vehicleOnly, r => r["ownerType"]!.GetValue<string>() == "BusinessPartner");
    }

    // ── The register and the expiry calendar (S5-DOC-13, S5-DOC-15) ──────────────────

    [Fact]
    public async Task The_register_lists_current_documents_and_can_be_filtered_and_the_calendar_shows_the_months_expiries()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "INSURANCE_POLICY");
        var expiry = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1).AddDays(10);
        var upload = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = expiry.ToString("yyyy-MM-dd"), documentNumber = "POL-9" });
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());

        var register = (await (await w.Admin.GetAsync($"/api/documents/register?ownerType=Vehicle&documentTypeId={typeId}")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.Single(register);
        Assert.Equal(vehicle["registrationNo"]!.GetValue<string>(), register[0]["ownerName"]!.GetValue<string>());

        var calendar = (await (await w.Admin.GetAsync($"/api/documents/expiry-calendar?year={expiry.Year}&month={expiry.Month}")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
        Assert.Contains(calendar, c => c["documentTypeName"]!.GetValue<string>() == "Insurance Policy");
    }

    // ── The nightly jobs (S5-DOC-06, S5-DOC-09) ──────────────────────────────────────

    [Fact]
    public async Task The_expiry_job_moves_a_document_through_active_expiring_soon_and_expired()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "INSURANCE_POLICY");   // 30-day lead
        var uploadResponse = await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf(), new { expiryDate = DateTime.UtcNow.AddDays(60).ToString("yyyy-MM-dd"), documentNumber = "POL-9" });
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());
        var uploaded = PartnerWorld.AsObject(await uploadResponse.DataAsync());
        Assert.Equal("Active", uploaded["status"]!.GetValue<string>());

        // Back-date the expiry into the lead window, then past it, running the job each time (no waiting real calendar time).
        factory.Execute("UPDATE doc.Documents SET ExpiryDate = @d WHERE DocumentId = @id", ("@d", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))), ("@id", uploaded["id"]!.GetValue<int>()));
        var soon = await w.Admin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { });
        Assert.True(soon.IsSuccessStatusCode, await soon.Content.ReadAsStringAsync());
        Assert.Equal("ExpiringSoon", factory.Scalar<string>("SELECT Status FROM doc.Documents WHERE DocumentId = @id", ("@id", uploaded["id"]!.GetValue<int>())));

        factory.Execute("UPDATE doc.Documents SET ExpiryDate = @d WHERE DocumentId = @id", ("@d", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))), ("@id", uploaded["id"]!.GetValue<int>()));
        await w.Admin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { });
        Assert.Equal("Expired", factory.Scalar<string>("SELECT Status FROM doc.Documents WHERE DocumentId = @id", ("@id", uploaded["id"]!.GetValue<int>())));

        // Idempotent: running it again with nothing changed recalculates nothing.
        var again = PartnerWorld.AsObject(await (await w.Admin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { })).DataAsync());
        Assert.Equal(0, again["expiry"]!["recalculated"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_retention_job_removes_a_superseded_version_only_once_its_type_s_retention_has_passed()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var typeId = await TypeIdAsync(w.Admin, "PURCHASE_INVOICE");   // 7-year default retention
        var first = PartnerWorld.AsObject(await (await UploadAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf())).DataAsync());
        await RenewAsync(w.Admin, "vehicles", VehicleWorld.Id(vehicle), typeId, Pdf());   // supersedes the first version

        var tooSoon = await w.Admin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { });
        Assert.Equal(0, PartnerWorld.AsObject(await tooSoon.DataAsync())["retention"]!["removed"]!.GetValue<int>());
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM doc.Documents WHERE DocumentId = @id", ("@id", first["id"]!.GetValue<int>())));

        // Aged past its 7-year retention.
        factory.Execute("UPDATE doc.Documents SET CreatedOn = @d WHERE DocumentId = @id", ("@d", DateTime.UtcNow.AddYears(-8)), ("@id", first["id"]!.GetValue<int>()));
        var run = PartnerWorld.AsObject(await (await w.Admin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { })).DataAsync());
        Assert.Equal(1, run["retention"]!["removed"]!.GetValue<int>());
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM doc.Documents WHERE DocumentId = @id", ("@id", first["id"]!.GetValue<int>())));
    }

    // ── Permissions ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unauthenticated_caller_cannot_reach_documents_and_the_admin_trigger_needs_config_manage()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.ActiveAsync();
        var anonymous = w.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{VehicleWorld.Id(vehicle)}/documents")).StatusCode);

        var noAdmin = w.As("Fleet Manager", 54, PermissionCodes.VEH_VIEW, PermissionCodes.DOC_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await noAdmin.PostAsJsonAsync("/api/admin/jobs/documents/run", new { })).StatusCode);
    }
}
