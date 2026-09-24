using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Shared.Authorization;
using VMS.Shared.Messages;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Vehicles;

/// <summary>S3-FIN-08, S2-VH-15, S3-FIN-10: the derived totals of a vehicle, the ledger, and the controlled adjustment route.</summary>
[Collection(ApiCollection.Name)]
public sealed class VehicleLedgerTests(ApiFactory factory)
{
    private static int Id(JsonObject v) => VehicleWorld.Id(v);
    private static string Day(int daysAgo = 0) => DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    /// <summary>
    /// An activated bank-leased vehicle: price 5,000,000 of which 2,000,000 was paid, a lease of 3,000,000 in installments of
    /// <paramref name="installment"/> over <paramref name="tenure"/> months from <paramref name="firstDue"/>, and an optional residual.
    /// </summary>
    private async Task<(VehicleWorld World, JsonObject Vehicle, int Bank)> LeasedAsync(VehicleWorld? world = null, decimal installment = 150_000, int tenure = 3, string firstDue = "2027-01-31", decimal? residual = 100_000, decimal? registration = 45_000, decimal? deposit = null)
    {
        var w = world ?? await VehicleWorld.CreateAsync(factory);
        var vehicle = await w.CreateAsync(w.Truck());
        var bank = await w.PartnerAsync("Bank");
        var block = PartnerWorld.AsObject(await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/acquisition")).DataAsync());
        Assert.True((await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/acquisition", new
        {
            acquisitionDate = Day(5), acquisitionType = "Lease", purchasePrice = 5_000_000, amountPaid = 2_000_000, paymentMode = "BankTransfer", registrationCost = registration,
            rowVersion = block["rowVersion"]!.GetValue<string>()
        })).IsSuccessStatusCode);
        var type = (await (await w.Admin.GetAsync("/api/lookups/FINANCE_TYPE")).DataAsync()).EnumerateArray().First(t => t.GetProperty("description").GetString() == "Bank Lease").GetProperty("id").GetInt32();
        var saved = await w.Admin.PutAsJsonAsync($"/api/vehicles/{Id(vehicle)}/finance", new
        {
            financeTypeId = type, bankId = bank, agreementNo = "AGR-" + Guid.NewGuid().ToString("N")[..8], agreementDate = Day(4), financeAmount = 3_000_000, downPayment = 2_000_000,
            installmentAmount = installment, frequency = "Monthly", tenure, firstDueDate = firstDue, residualAmount = residual, securityDeposit = deposit
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await w.UploadRegistrationBookAsync(Id(vehicle));   // BR-VH-015: a Bank Leased vehicle needs one on file to activate
        var fresh = await w.GetAsync(Id(vehicle));
        var activated = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/activate", new { category = "BankLeased", details = new { }, rowVersion = fresh["rowVersion"]!.GetValue<string>() });
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());
        return (w, vehicle, bank);
    }

    private static async Task<JsonObject> SummaryAsync(VehicleWorld w, JsonObject vehicle, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/financial-summary");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    private static async Task<List<JsonObject>> ScheduleAsync(VehicleWorld w, JsonObject vehicle) =>
        (await (await w.Admin.GetAsync($"/api/vehicles/{Id(vehicle)}/installments")).DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();

    private static async Task<JsonObject> PayAsync(VehicleWorld w, JsonObject vehicle, int installmentNo, decimal amount)
    {
        var row = (await ScheduleAsync(w, vehicle)).First(r => r["installmentNo"]!.GetValue<int>() == installmentNo);
        var response = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/installments/{row["id"]!.GetValue<int>()}/payments", new { amount, paidOn = Day(1), rowVersion = row["rowVersion"]!.GetValue<string>() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return PartnerWorld.AsObject(await response.DataAsync());
    }

    private static async Task<List<JsonObject>> LedgerAsync(VehicleWorld w, JsonObject vehicle, HttpClient? client = null)
    {
        var response = await (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/transactions");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.DataAsync()).EnumerateArray().Select(PartnerWorld.AsObject).ToList();
    }

    private static Task<HttpResponseMessage> Reverse(VehicleWorld w, JsonObject vehicle, int transactionId, object? body = null, HttpClient? client = null) =>
        (client ?? w.Admin).PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/transactions/{transactionId}/reverse", body ?? new { reason = "Entered in error" });

    // ── Receipts (S3-FIN-14) ─────────────────────────────────────────────────────────

    private static byte[] Pdf(int length = 400)
    {
        var bytes = new byte[Math.Max(length, 12)];
        Random.Shared.NextBytes(bytes);
        "%PDF-1.4\n"u8.CopyTo(bytes);
        return bytes;
    }

    private static async Task<HttpResponseMessage> UploadReceipt(VehicleWorld w, JsonObject vehicle, int installmentId, int transactionId, byte[] content, string name = "receipt.pdf", HttpClient? client = null)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", name);
        return await (client ?? w.Admin).PostAsync($"/api/vehicles/{Id(vehicle)}/installments/{installmentId}/payments/{transactionId}/receipt", form);
    }

    private static Task<HttpResponseMessage> DownloadReceipt(VehicleWorld w, JsonObject vehicle, int transactionId, HttpClient? client = null) =>
        (client ?? w.Admin).GetAsync($"/api/vehicles/{Id(vehicle)}/transactions/{transactionId}/receipt");

    [Fact]
    public async Task A_receipt_is_attached_to_a_payment_and_can_be_downloaded_back_unaltered()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        var installmentId = (await ScheduleAsync(w, vehicle))[0]["id"]!.GetValue<int>();
        var transactionId = paid["transactionId"]!.GetValue<int>();
        var content = Pdf(1500);

        var upload = await UploadReceipt(w, vehicle, installmentId, transactionId, content, "Cheque scan.pdf");
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var saved = PartnerWorld.AsObject(await upload.DataAsync());
        Assert.True(saved["hasReceipt"]!.GetValue<bool>());
        Assert.Equal("Cheque scan.pdf", saved["receiptFileName"]!.GetValue<string>());

        var entry = (await LedgerAsync(w, vehicle)).Single(l => l["id"]!.GetValue<int>() == transactionId);
        Assert.True(entry["hasReceipt"]!.GetValue<bool>());
        Assert.Equal("Cheque scan.pdf", entry["receiptFileName"]!.GetValue<string>());

        var download = await DownloadReceipt(w, vehicle, transactionId);
        Assert.True(download.IsSuccessStatusCode, await download.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("Cheque scan.pdf", download.Content.Headers.ContentDisposition!.FileName?.Trim('"'));
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition.DispositionType);
    }

    [Fact]
    public async Task Only_a_scan_or_a_pdf_is_accepted_and_a_receipt_is_attached_once()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        var installmentId = (await ScheduleAsync(w, vehicle))[0]["id"]!.GetValue<int>();
        var transactionId = paid["transactionId"]!.GetValue<int>();

        var notAScan = await UploadReceipt(w, vehicle, installmentId, transactionId, "just some text pretending to be a pdf"u8.ToArray(), "receipt.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, notAScan.StatusCode);
        Assert.Contains("PDF, PNG or JPEG", await notAScan.Content.ReadAsStringAsync());

        var first = await UploadReceipt(w, vehicle, installmentId, transactionId, Pdf());
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var second = await UploadReceipt(w, vehicle, installmentId, transactionId, Pdf());
        var errors = await PartnerWorld.ErrorsAsync(second);
        Assert.Contains(errors, e => e is { Field: "file", Code: Msg.VhReceiptAlreadyAttached });
    }

    [Fact]
    public async Task A_receipt_is_refused_for_the_wrong_installment_or_the_wrong_entry_and_an_empty_upload_is_refused()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        var schedule = await ScheduleAsync(w, vehicle);
        var installmentId = schedule[0]["id"]!.GetValue<int>();
        var otherInstallmentId = schedule[1]["id"]!.GetValue<int>();
        var transactionId = paid["transactionId"]!.GetValue<int>();
        var acquisitionEntry = (await LedgerAsync(w, vehicle)).First(l => l["type"]!.GetValue<string>() == "Acquisition")["id"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.NotFound, (await UploadReceipt(w, vehicle, otherInstallmentId, transactionId, Pdf())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadReceipt(w, vehicle, installmentId, acquisitionEntry, Pdf())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadReceipt(w, vehicle, installmentId, 999_999, Pdf())).StatusCode);

        using var empty = new MultipartFormDataContent();
        var response = await w.Admin.PostAsync($"/api/vehicles/{Id(vehicle)}/installments/{installmentId}/payments/{transactionId}/receipt", empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.False((await LedgerAsync(w, vehicle)).Single(l => l["id"]!.GetValue<int>() == transactionId)["hasReceipt"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Downloading_a_receipt_that_was_never_attached_is_not_found()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        Assert.Equal(HttpStatusCode.NotFound, (await DownloadReceipt(w, vehicle, paid["transactionId"]!.GetValue<int>())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DownloadReceipt(w, vehicle, 999_999)).StatusCode);
    }

    [Fact]
    public async Task Only_someone_who_may_pay_installments_can_attach_a_receipt_and_only_someone_who_may_see_cost_can_download_one()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var first = await PayAsync(w, vehicle, 1, 150_000);
        var second = await PayAsync(w, vehicle, 2, 150_000);
        var schedule = await ScheduleAsync(w, vehicle);
        var firstInstallmentId = schedule[0]["id"]!.GetValue<int>();
        var secondInstallmentId = schedule[1]["id"]!.GetValue<int>();

        var noPay = w.As("Viewer", 6, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_FIELD_COST_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadReceipt(w, vehicle, secondInstallmentId, second["transactionId"]!.GetValue<int>(), Pdf(), client: noPay)).StatusCode);

        Assert.True((await UploadReceipt(w, vehicle, firstInstallmentId, first["transactionId"]!.GetValue<int>(), Pdf())).IsSuccessStatusCode);

        var noCostView = w.As("Payer", 7, PermissionCodes.VEH_VIEW, PermissionCodes.FIN_INSTALLMENT_PAY);
        Assert.Equal(HttpStatusCode.Forbidden, (await DownloadReceipt(w, vehicle, first["transactionId"]!.GetValue<int>(), noCostView)).StatusCode);
        Assert.True((await DownloadReceipt(w, vehicle, first["transactionId"]!.GetValue<int>())).IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_receipt_whose_recorded_checksum_no_longer_matches_its_bytes_is_not_served()
    {
        // The file on disk is untouched; only the database's own record of its checksum has drifted from it.
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        var installmentId = (await ScheduleAsync(w, vehicle))[0]["id"]!.GetValue<int>();
        var transactionId = paid["transactionId"]!.GetValue<int>();
        Assert.True((await UploadReceipt(w, vehicle, installmentId, transactionId, Pdf())).IsSuccessStatusCode);

        factory.Execute("UPDATE veh.VehicleTransactions SET ReceiptSha256 = @s WHERE VehicleTransactionId = @t", ("@s", new string('0', 64)), ("@t", transactionId));

        Assert.Equal(HttpStatusCode.Conflict, (await DownloadReceipt(w, vehicle, transactionId)).StatusCode);
    }

    [Fact]
    public async Task A_receipt_altered_on_disk_after_it_was_stored_is_not_served()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        var installmentId = (await ScheduleAsync(w, vehicle))[0]["id"]!.GetValue<int>();
        var transactionId = paid["transactionId"]!.GetValue<int>();
        Assert.True((await UploadReceipt(w, vehicle, installmentId, transactionId, Pdf())).IsSuccessStatusCode);

        var key = factory.Scalar<string>("SELECT ReceiptStorageKey FROM veh.VehicleTransactions WHERE VehicleTransactionId = @t", ("@t", transactionId));
        var path = Path.Combine(factory.FileRoot, key.Replace('/', Path.DirectorySeparatorChar));
        var onDisk = await File.ReadAllBytesAsync(path);
        onDisk[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, onDisk);

        Assert.Equal(HttpStatusCode.Conflict, (await DownloadReceipt(w, vehicle, transactionId)).StatusCode);
    }

    // ── Derived totals ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paid_to_date_is_the_initial_payment_plus_installments_and_moves_the_moment_one_is_recorded()
    {
        var (w, vehicle, _) = await LeasedAsync(registration: null);
        var start = await SummaryAsync(w, vehicle);
        Assert.Equal(5_000_000m, start["acquisitionCost"]!.GetValue<decimal>());
        Assert.Equal(2_000_000m, start["paidToDate"]!.GetValue<decimal>());   // AC-VH-003
        Assert.Equal(0m, start["majorExpenses"]!.GetValue<decimal>());
        Assert.Equal(5_000_000m, start["totalCost"]!.GetValue<decimal>());

        await PayAsync(w, vehicle, 1, 150_000);
        Assert.Equal(2_150_000m, (await SummaryAsync(w, vehicle))["paidToDate"]!.GetValue<decimal>());   // AC-VH-004: no batch run
    }

    [Fact]
    public async Task Cost_counts_registration_and_item_costs_as_major_expenses_and_deposits_stand_apart()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var item = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/items", new { itemTypeId = w.ItemType, description = "40 ft container", installationDate = Day(2), cost = 450_000 });
        Assert.True(item.IsSuccessStatusCode, await item.Content.ReadAsStringAsync());

        var summary = await SummaryAsync(w, vehicle);
        Assert.Equal(5_000_000m, summary["acquisitionCost"]!.GetValue<decimal>());
        Assert.Equal(495_000m, summary["majorExpenses"]!.GetValue<decimal>());   // 45,000 registration + 450,000 container
        Assert.Equal(5_495_000m, summary["totalCost"]!.GetValue<decimal>());
        Assert.Equal(0m, summary["deposits"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task A_security_deposit_is_held_apart_from_what_the_vehicle_cost_and_what_was_paid()
    {
        var (w, vehicle, _) = await LeasedAsync(deposit: 50_000);
        var summary = await SummaryAsync(w, vehicle);
        Assert.Equal(50_000m, summary["deposits"]!.GetValue<decimal>());
        Assert.Equal(5_045_000m, summary["totalCost"]!.GetValue<decimal>());
        Assert.Equal(2_000_000m, summary["paidToDate"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task The_schedule_of_a_lease_matches_the_acceptance_criteria_and_the_outstanding_is_total_plus_residual_less_paid()
    {
        // AC-VH-005 and AC-VH-006's neighbours: 75,000 for 36 months from 10-Oct-2026.
        var (w, vehicle, _) = await LeasedAsync(installment: 75_000, tenure: 36, firstDue: "2026-10-10", residual: null);
        var schedule = await ScheduleAsync(w, vehicle);
        Assert.Equal(36, schedule.Count);
        Assert.Equal("2026-10-10", schedule[0]["dueDate"]!.GetValue<string>());
        Assert.Equal("2029-09-10", schedule[^1]["dueDate"]!.GetValue<string>());

        var agreement = (await SummaryAsync(w, vehicle))["agreement"]!.AsObject();
        Assert.Equal(2_700_000m, agreement["totalPayable"]!.GetValue<decimal>());
        Assert.Equal(2_700_000m, agreement["outstanding"]!.GetValue<decimal>());

        await PayAsync(w, vehicle, 1, 75_000);
        await PayAsync(w, vehicle, 2, 30_000);   // a part payment
        agreement = (await SummaryAsync(w, vehicle))["agreement"]!.AsObject();
        Assert.Equal(2_595_000m, agreement["outstanding"]!.GetValue<decimal>());
        Assert.Equal(1, agreement["installmentsPaid"]!.GetValue<int>());
        Assert.Equal(36, agreement["installmentsTotal"]!.GetValue<int>());
        Assert.Equal("2026-11-10", agreement["nextDueDate"]!.GetValue<string>());
        Assert.Equal(45_000m, agreement["nextDueAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task A_residual_is_part_of_what_is_outstanding_and_a_settled_agreement_owes_nothing()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var agreement = (await SummaryAsync(w, vehicle))["agreement"]!.AsObject();
        Assert.Equal(450_000m, agreement["totalPayable"]!.GetValue<decimal>());
        Assert.Equal(100_000m, agreement["residual"]!.GetValue<decimal>());
        Assert.Equal(550_000m, agreement["outstanding"]!.GetValue<decimal>());

        foreach (var (no, amount) in new[] { (1, 150_000m), (2, 150_000m), (3, 150_000m), (4, 100_000m) }) await PayAsync(w, vehicle, no, amount);
        var settled = await SummaryAsync(w, vehicle);
        Assert.Equal("Settled", settled["agreement"]!["status"]!.GetValue<string>());
        Assert.Equal(0m, settled["agreement"]!["outstanding"]!.GetValue<decimal>());
        Assert.Null(settled["agreement"]!["nextDueDate"]);
        Assert.Equal(2_550_000m, settled["paidToDate"]!.GetValue<decimal>());   // 2,000,000 down payment + 550,000 paid on the schedule
    }

    [Fact]
    public async Task What_is_overdue_is_counted_from_todays_date_and_never_stored()
    {
        var (w, vehicle, _) = await LeasedAsync();
        factory.Execute("UPDATE veh.VehicleInstallments SET DueDate = DATEADD(day, -20 + (InstallmentNo * 5), CAST(SYSDATETIME() AS date)) WHERE VehicleId = @v AND InstallmentNo <= 2", ("@v", Id(vehicle)));
        await PayAsync(w, vehicle, 1, 50_000);   // part paid, still overdue
        var agreement = (await SummaryAsync(w, vehicle))["agreement"]!.AsObject();
        Assert.Equal(2, agreement["overdueCount"]!.GetValue<int>());
        Assert.Equal(250_000m, agreement["overdueAmount"]!.GetValue<decimal>());   // 100,000 left on the first, 150,000 on the second
        Assert.Equal(100_000m, agreement["nextDueAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task A_vehicle_without_an_agreement_has_none_and_a_draft_has_no_money_yet()
    {
        var w = await VehicleWorld.CreateAsync(factory);
        var draft = await w.CreateAsync(w.Truck());
        var summary = await SummaryAsync(w, draft);
        Assert.Equal(0m, summary["paidToDate"]!.GetValue<decimal>());
        Assert.Null(summary["agreement"]);
    }

    [Fact]
    public async Task Each_figure_is_shown_only_to_someone_who_may_see_it()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        var bare = await SummaryAsync(w, vehicle, viewer);
        foreach (var field in new[] { "acquisitionCost", "majorExpenses", "totalCost", "paidToDate", "deposits" }) Assert.False(bare.ContainsKey(field), field);
        foreach (var field in new[] { "totalPayable", "residual", "outstanding", "nextDueAmount", "overdueAmount" }) Assert.False(bare["agreement"]!.AsObject().ContainsKey(field), field);
        Assert.Equal(4, bare["agreement"]!["installmentsTotal"]!.GetValue<int>());   // the counts are not money

        var costOnly = await SummaryAsync(w, vehicle, w.As("Cost", 7, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_FIELD_COST_VIEW));
        Assert.True(costOnly.ContainsKey("paidToDate"));
        Assert.False(costOnly["agreement"]!.AsObject().ContainsKey("outstanding"));

        var financeOnly = await SummaryAsync(w, vehicle, w.As("Finance", 8, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_FIELD_FINANCE_VIEW));
        Assert.False(financeOnly.ContainsKey("paidToDate"));
        Assert.True(financeOnly["agreement"]!.AsObject().ContainsKey("outstanding"));
    }

    // ── BR-VH-007, now answered by the schedule itself ──────────────────────────────

    [Fact]
    public async Task The_category_cannot_leave_bank_leased_while_the_schedule_has_a_balance()
    {
        var (w, vehicle, _) = await LeasedAsync(tenure: 1, residual: null);
        var body = new { category = "SelfOwned", details = new { } };
        await PartnerWorld.AssertRefusedAsync(await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/category", body), "category", Msg.VhCategoryLockedByFinance);

        await PayAsync(w, vehicle, 1, 150_000);
        var released = await w.Admin.PostAsJsonAsync($"/api/vehicles/{Id(vehicle)}/category", body);
        Assert.True(released.IsSuccessStatusCode, await released.Content.ReadAsStringAsync());
    }

    // ── The controlled adjustment route ─────────────────────────────────────────────

    [Fact]
    public async Task The_ledger_lists_every_entry_and_never_offers_to_delete_one()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var ledger = await LedgerAsync(w, vehicle);
        Assert.Equal(new[] { "Acquisition", "InitialPayment", "MajorExpense" }.Order(), ledger.Select(l => l["type"]!.GetValue<string>()).Order());
        Assert.All(ledger, l => Assert.False(l["isReversed"]!.GetValue<bool>()));
        var deleted = await w.Admin.DeleteAsync($"/api/vehicles/{Id(vehicle)}/transactions/{ledger[0]["id"]!.GetValue<int>()}");
        Assert.True(deleted.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, deleted.StatusCode.ToString());   // there is no such route
        Assert.Equal(ledger.Count, (await LedgerAsync(w, vehicle)).Count);

        var viewer = w.As("Viewer", 6, PermissionCodes.VEH_VIEW);
        Assert.All(await LedgerAsync(w, vehicle, viewer), l => Assert.False(l.ContainsKey("amount")));
    }

    [Fact]
    public async Task Reversing_an_entry_posts_a_negative_entry_with_a_reason_and_leaves_the_original_alone()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var registration = (await LedgerAsync(w, vehicle)).Single(l => l["subType"]?.GetValue<string>() == "Registration");
        var id = registration["id"]!.GetValue<int>();

        var response = await Reverse(w, vehicle, id, new { reason = "Charged to the wrong vehicle" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var adjustment = PartnerWorld.AsObject(await response.DataAsync());
        Assert.Equal("Adjustment", adjustment["type"]!.GetValue<string>());
        Assert.Equal("MajorExpense", adjustment["subType"]!.GetValue<string>());
        Assert.Equal(-45_000m, adjustment["amount"]!.GetValue<decimal>());
        Assert.Equal(id, adjustment["reversesTransactionId"]!.GetValue<int>());
        Assert.Equal("Charged to the wrong vehicle", adjustment["reason"]!.GetValue<string>());
        Assert.False(adjustment["isSystemGenerated"]!.GetValue<bool>());

        var ledger = await LedgerAsync(w, vehicle);
        var original = ledger.Single(l => l["id"]!.GetValue<int>() == id);
        Assert.Equal(45_000m, original["amount"]!.GetValue<decimal>());   // the entry itself is untouched
        Assert.True(original["isReversed"]!.GetValue<bool>());
        Assert.Equal(0m, (await SummaryAsync(w, vehicle))["majorExpenses"]!.GetValue<decimal>());   // and the total no longer counts it
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleTransactionId = @i", ("@i", id)));
    }

    [Fact]
    public async Task Reversing_an_installment_payment_reopens_the_installment_and_an_agreement_it_settled()
    {
        var (w, vehicle, _) = await LeasedAsync(tenure: 1, residual: null);
        var paid = await PayAsync(w, vehicle, 1, 150_000);
        Assert.True(paid["agreementSettled"]!.GetValue<bool>());
        Assert.Equal(2_150_000m, (await SummaryAsync(w, vehicle))["paidToDate"]!.GetValue<decimal>());

        var reversed = await Reverse(w, vehicle, paid["transactionId"]!.GetValue<int>(), new { reason = "Cheque bounced" });
        Assert.True(reversed.IsSuccessStatusCode, await reversed.Content.ReadAsStringAsync());

        var row = (await ScheduleAsync(w, vehicle))[0];
        Assert.Equal("Pending", row["status"]!.GetValue<string>());
        Assert.Equal(0m, row["paidAmount"]!.GetValue<decimal>());
        Assert.Null(row["paidOn"]);
        Assert.Equal("Active", factory.Scalar<string>("SELECT Status FROM veh.VehicleFinanceAgreements WHERE VehicleId = @v", ("@v", Id(vehicle))));
        var summary = await SummaryAsync(w, vehicle);
        Assert.Equal(2_000_000m, summary["paidToDate"]!.GetValue<decimal>());
        Assert.Equal(150_000m, summary["agreement"]!["outstanding"]!.GetValue<decimal>());

        // It can be paid again, now that it is open.
        await PayAsync(w, vehicle, 1, 150_000);
        Assert.Equal(2_150_000m, (await SummaryAsync(w, vehicle))["paidToDate"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Reversing_one_of_two_part_payments_leaves_the_other_paid()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var first = await PayAsync(w, vehicle, 1, 100_000);
        await PayAsync(w, vehicle, 1, 50_000);
        await Reverse(w, vehicle, first["transactionId"]!.GetValue<int>(), new { reason = "Wrong amount" });

        var row = (await ScheduleAsync(w, vehicle))[0];
        Assert.Equal("PartiallyPaid", row["status"]!.GetValue<string>());
        Assert.Equal(50_000m, row["paidAmount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task An_entry_is_reversed_once_a_reversal_is_not_reversed_and_the_rest_is_checked()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var target = (await LedgerAsync(w, vehicle)).First(l => l["type"]!.GetValue<string>() == "Acquisition")["id"]!.GetValue<int>();

        async Task<List<(string Field, string Code, string Message)>> Errors(int id, object body) => await PartnerWorld.ErrorsAsync(await Reverse(w, vehicle, id, body));
        Assert.Contains(await Errors(target, new { }), e => e is { Field: "reason", Code: Msg.Required });
        Assert.Contains(await Errors(target, new { reason = "  " }), e => e is { Field: "reason", Code: Msg.Required });
        Assert.Contains(await Errors(target, new { reason = new string('x', 501) }), e => e is { Field: "reason", Code: Msg.MaxLength });
        Assert.Contains(await Errors(target, new { reason = "x", date = Day(-3) }), e => e is { Field: "date", Code: Msg.NotFuture });
        Assert.Contains(await Errors(target, new { reason = "x", date = Day(30) }), e => e is { Field: "date", Code: Msg.Min });   // not before the entry it reverses
        Assert.DoesNotContain(await LedgerAsync(w, vehicle), l => l["type"]!.GetValue<string>() == "Adjustment");

        var first = await Reverse(w, vehicle, target, new { reason = "Duplicate" });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        await PartnerWorld.AssertRefusedAsync(await Reverse(w, vehicle, target), "transactionId", Msg.VhAlreadyReversed);

        var adjustment = (await LedgerAsync(w, vehicle)).Single(l => l["type"]!.GetValue<string>() == "Adjustment")["id"]!.GetValue<int>();
        await PartnerWorld.AssertRefusedAsync(await Reverse(w, vehicle, adjustment), "transactionId", Msg.VhReversalNotReversible);
        Assert.Equal(HttpStatusCode.NotFound, (await Reverse(w, vehicle, 999_999)).StatusCode);
    }

    [Fact]
    public async Task An_entry_of_another_vehicle_is_not_found_and_only_someone_who_may_post_adjustments_can_reverse()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var (_, other, _) = await LeasedAsync(w);
        var foreign = (await LedgerAsync(w, other))[0]["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NotFound, (await Reverse(w, vehicle, foreign)).StatusCode);

        var own = (await LedgerAsync(w, vehicle))[0]["id"]!.GetValue<int>();
        var noAdjust = w.As("Finance", 8, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_FIELD_COST_VIEW, PermissionCodes.VEH_FIELD_FINANCE_VIEW);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reverse(w, vehicle, own, client: noAdjust)).StatusCode);
        var blind = w.As("Blind", 9, PermissionCodes.VEH_VIEW, PermissionCodes.FIN_ADJUSTMENT_POST);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reverse(w, vehicle, own, client: blind)).StatusCode);   // may not see what it reverses
        Assert.DoesNotContain(await LedgerAsync(w, vehicle), l => l["type"]!.GetValue<string>() == "Adjustment");
    }

    [Fact]
    public async Task Two_reversals_of_the_same_entry_at_once_post_one()
    {
        var (w, vehicle, _) = await LeasedAsync();
        var target = (await LedgerAsync(w, vehicle)).First(l => l["type"]!.GetValue<string>() == "Acquisition")["id"]!.GetValue<int>();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reverse(w, vehicle, target)));
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Equal(1, factory.Scalar<int>("SELECT COUNT(*) FROM veh.VehicleTransactions WHERE VehicleId = @v AND ReversesTransactionId = @t", ("@v", Id(vehicle)), ("@t", target)));
    }
}
