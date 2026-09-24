using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using VMS.Modules.Vehicles.Models;
using VMS.Modules.Vehicles.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Vehicles.Controllers;

/// <summary>Vehicles: the fleet, from a Draft being entered to a sold vehicle (FSD Module B).</summary>
[ApiController]
[Route("api/vehicles")]
public class VehiclesController(
    IVehicleService vehicles, IVehicleQueryService queries, ILifecycleService lifecycle, ICategoryService categories,
    IItemService items, IOdometerService odometer, IAssignmentService assignments, IAcquisitionService acquisitions, IFinanceService finance, IInstallmentService installments, IActivationService activation, IFinancialSummaryService summaries, ITransactionService transactions) : ControllerBase
{
    private VehicleCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    // ── The vehicle ─────────────────────────────────────────────────────────────────

    [HttpGet]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> List([FromQuery] VehicleListQuery query) =>
        Ok(ApiResponse<PaginatedResponse<VehicleListItem>>.Ok(await queries.ListAsync(query)));

    /// <summary>The filtered list as an Excel file.</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionCodes.VEH_EXPORT)]
    public async Task<IActionResult> Export([FromQuery] VehicleListQuery query)
    {
        var (content, fileName) = await queries.ExportAsync(query);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Get(int id) => Ok(ApiResponse<VehicleModel>.Ok(await vehicles.GetAsync(id)));

    /// <summary>Saves a new vehicle as a Draft: the vehicle row only, no postings (FR-VH-012).</summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.VEH_CREATE)]
    public async Task<IActionResult> Create([FromBody] CreateVehicleRequest request)
    {
        var created = await vehicles.CreateAsync(request, Caller);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<VehicleModel>.Ok(created, "Vehicle saved."));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.VEH_EDIT)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateVehicleRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await vehicles.UpdateAsync(id, request, Caller), "Vehicle saved."));

    // ── Acquisition ─────────────────────────────────────────────────────────────────

    /// <summary>The acquisition block: when and how the vehicle was acquired, what was paid (FSD §18.1). Amounts show only with <c>VEH.FIELD.COST.VIEW</c>.</summary>
    [HttpGet("{id:int}/acquisition")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Acquisition(int id) => Ok(ApiResponse<AcquisitionModel>.Ok(await acquisitions.GetAsync(id)));

    /// <summary>Saves the acquisition block of a Draft. Nothing is posted until the vehicle is activated (§19).</summary>
    [HttpPut("{id:int}/acquisition")]
    [RequirePermission(PermissionCodes.VEH_ACQUISITION_EDIT)]
    public async Task<IActionResult> SaveAcquisition(int id, [FromBody] SaveAcquisitionRequest request) =>
        Ok(ApiResponse<AcquisitionModel>.Ok(await acquisitions.SaveAsync(id, request, Caller), "Acquisition saved."));

    // ── Finance ─────────────────────────────────────────────────────────────────────

    /// <summary>The vehicle's open finance agreement (FSD §18.2), or null data when it has none. Amounts show only with <c>VEH.FIELD.FINANCE.VIEW</c>.</summary>
    [HttpGet("{id:int}/finance")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Finance(int id) => Ok(ApiResponse<AgreementModel?>.Ok(await finance.GetAsync(id)));

    /// <summary>Saves the finance block of a Draft. Sending the same terms again without <c>confirmMismatch</c> is refused when the finance amount and down payment do not add up to the price (BR-VH-010).</summary>
    [HttpPut("{id:int}/finance")]
    [RequirePermission(PermissionCodes.VEH_ACQUISITION_EDIT)]
    public async Task<IActionResult> SaveFinance(int id, [FromBody] SaveFinanceRequest request) =>
        Ok(ApiResponse<AgreementModel>.Ok(await finance.SaveAsync(id, request, Caller), "Finance saved."));

    [HttpDelete("{id:int}/finance")]
    [RequirePermission(PermissionCodes.VEH_ACQUISITION_EDIT)]
    public async Task<IActionResult> RemoveFinance(int id)
    {
        await finance.RemoveAsync(id, Caller);
        return Ok(ApiResponse.Ok("Finance removed."));
    }

    /// <summary>The schedule a Draft's saved agreement would generate, for the preview with editable due dates (FR-VH-005). Nothing is written.</summary>
    [HttpGet("{id:int}/finance/schedule")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> SchedulePreview(int id) => Ok(ApiResponse<List<ScheduleRowModel>>.Ok(await activation.SchedulePreviewAsync(id)));

    // ── Activation ──────────────────────────────────────────────────────────────────

    /// <summary>What is missing before a Draft can be activated, and the postings and schedule that activating would write. Writes nothing.</summary>
    [HttpPost("{id:int}/activation-check")]
    [RequirePermission(PermissionCodes.VEH_ACTIVATE)]
    public async Task<IActionResult> CheckActivation(int id, [FromBody] ActivateVehicleRequest request) =>
        Ok(ApiResponse<ActivationCheck>.Ok(await activation.CheckAsync(id, request, Caller)));

    /// <summary>Activates a Draft: everything is written in one transaction, or nothing is (FR-VH-012).</summary>
    [HttpPost("{id:int}/activate")]
    [RequirePermission(PermissionCodes.VEH_ACTIVATE)]
    public async Task<IActionResult> Activate(int id, [FromBody] ActivateVehicleRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await activation.ActivateAsync(id, request, Caller), "Vehicle activated."));

    /// <summary>Paid to date, cost, and what is outstanding on the bank agreement, worked out from the ledger and the schedule each time (BR-VH-003). Each figure shows only with its field permission.</summary>
    [HttpGet("{id:int}/financial-summary")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> FinancialSummary(int id) => Ok(ApiResponse<FinancialSummary>.Ok(await summaries.GetAsync(id)));
    /// <summary>The vehicle's ledger, newest first, adjustments included. Amounts show only with <c>VEH.FIELD.COST.VIEW</c>.</summary>
    [HttpGet("{id:int}/transactions")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Transactions(int id) => Ok(ApiResponse<List<TransactionModel>>.Ok(await transactions.ListAsync(id)));

    /// <summary>The controlled adjustment route (BR-VH-011): posts a reversing entry with a reason. The entry itself is never changed or deleted.</summary>
    [HttpPost("{id:int}/transactions/{transactionId:int}/reverse")]
    [RequirePermission(PermissionCodes.FIN_ADJUSTMENT_POST)]
    public async Task<IActionResult> Reverse(int id, int transactionId, [FromBody] ReverseTransactionRequest request) =>
        Ok(ApiResponse<TransactionModel>.Ok(await transactions.ReverseAsync(id, transactionId, request, Caller), "Entry reversed."));
    /// <summary>The installment schedule of the vehicle's active agreement, in due order.</summary>
    [HttpGet("{id:int}/installments")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Installments(int id) => Ok(ApiResponse<List<InstallmentModel>>.Ok(await installments.ListAsync(id)));

    /// <summary>Records a payment against one installment: updates the schedule and posts a ledger entry.</summary>
    [HttpPost("{id:int}/installments/{installmentId:int}/payments")]
    [RequirePermission(PermissionCodes.FIN_INSTALLMENT_PAY)]
    public async Task<IActionResult> PayInstallment(int id, int installmentId, [FromBody] PayInstallmentRequest request) =>
        Ok(ApiResponse<InstallmentPaymentModel>.Ok(await installments.PayAsync(id, installmentId, request, Caller), "Payment recorded."));

    /// <summary>Attaches a receipt to the ledger entry of an installment payment (source §7). Once attached it stays: nothing here is replaced or removed.</summary>
    [HttpPost("{id:int}/installments/{installmentId:int}/payments/{transactionId:int}/receipt")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequirePermission(PermissionCodes.FIN_INSTALLMENT_PAY)]
    public async Task<IActionResult> UploadReceipt(int id, int installmentId, int transactionId, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        var saved = await transactions.AttachReceiptAsync(id, installmentId, transactionId, stream, file.FileName, Caller);
        return Ok(ApiResponse<TransactionModel>.Ok(saved, "Receipt attached."));
    }

    /// <summary>The receipt attached to a ledger entry. Amounts are behind <c>VEH.FIELD.COST.VIEW</c>; so is the receipt that shows them.</summary>
    [HttpGet("{id:int}/transactions/{transactionId:int}/receipt")]
    [RequirePermission(PermissionCodes.VEH_FIELD_COST_VIEW)]
    public async Task<IActionResult> DownloadReceipt(int id, int transactionId)
    {
        var file = await transactions.OpenReceiptAsync(id, transactionId);
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType);
    }

    // ── Life in the fleet ───────────────────────────────────────────────────────────

    /// <summary>Moves a vehicle in the fleet between Active, Under Maintenance and Temporarily Unavailable (or reinstates a Retired one).</summary>
    [HttpPost("{id:int}/status")]
    [RequirePermission(PermissionCodes.VEH_STATUS_CHANGE)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeStatusRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await lifecycle.ChangeStatusAsync(id, request, Caller), "Status changed."));

    /// <summary>Changes the ownership category: closes the open relation and opens a new one (BR-VH-005).</summary>
    [HttpPost("{id:int}/category")]
    [RequirePermission(PermissionCodes.VEH_CATEGORY_CHANGE)]
    public async Task<IActionResult> ChangeCategory(int id, [FromBody] ChangeCategoryRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await categories.ChangeAsync(id, request, Caller), "Category changed."));

    /// <summary>Retires, sells or transfers the vehicle (BR-VH-023).</summary>
    [HttpPost("{id:int}/dispose")]
    [RequirePermission(PermissionCodes.VEH_DISPOSE)]
    public async Task<IActionResult> Dispose(int id, [FromBody] DisposeRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await lifecycle.DisposeAsync(id, request, Caller), "Vehicle updated."));

    /// <summary>Every change to the vehicle and what belongs to it, newest first, and its lifecycle.</summary>
    [HttpGet("{id:int}/history")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> History(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(ApiResponse<VehicleHistory>.Ok(await lifecycle.HistoryAsync(id, page, pageSize, User)));

    // ── Driver ──────────────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/driver")]
    [RequirePermission(PermissionCodes.VEH_DRIVER_ASSIGN)]
    public async Task<IActionResult> AssignDriver(int id, [FromBody] AssignDriverRequest request) =>
        Ok(ApiResponse<VehicleModel>.Ok(await assignments.AssignAsync(id, request, Caller), "Driver assigned."));

    [HttpDelete("{id:int}/driver")]
    [RequirePermission(PermissionCodes.VEH_DRIVER_ASSIGN)]
    public async Task<IActionResult> ReleaseDriver(int id, [FromQuery] DateOnly? date, [FromQuery] string? reason) =>
        Ok(ApiResponse<VehicleModel>.Ok(await assignments.ReleaseAsync(id, new ReleaseDriverRequest { Date = date, Reason = reason }, Caller), "Driver released."));

    // ── Attached items ──────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/items")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Items(int id, [FromQuery] bool includeGone = false) =>
        Ok(ApiResponse<List<ItemModel>>.Ok(await items.ListAsync(id, includeGone)));

    [HttpPost("{id:int}/items")]
    [RequirePermission(PermissionCodes.VEH_ITEM_MANAGE)]
    public async Task<IActionResult> Attach(int id, [FromBody] AttachItemRequest request) =>
        Ok(ApiResponse<ItemModel>.Ok(await items.AttachAsync(id, request, Caller), "Item attached."));

    [HttpPost("{id:int}/items/{itemId:int}/detach")]
    [RequirePermission(PermissionCodes.VEH_ITEM_MANAGE)]
    public async Task<IActionResult> Detach(int id, int itemId, [FromBody] DetachItemRequest request) =>
        Ok(ApiResponse<ItemModel>.Ok(await items.DetachAsync(id, itemId, request, Caller), "Item detached."));

    [HttpPost("{id:int}/items/{itemId:int}/transfer")]
    [RequirePermission(PermissionCodes.VEH_ITEM_MANAGE)]
    public async Task<IActionResult> Transfer(int id, int itemId, [FromBody] TransferItemRequest request) =>
        Ok(ApiResponse<ItemModel>.Ok(await items.TransferAsync(id, itemId, request, Caller), "Item transferred."));

    // ── Odometer ────────────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/odometer")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Odometer(int id) => Ok(ApiResponse<List<OdometerModel>>.Ok(await odometer.ListAsync(id)));

    [HttpPost("{id:int}/odometer")]
    [RequirePermission(PermissionCodes.VEH_EDIT)]
    public async Task<IActionResult> AddOdometer(int id, [FromBody] AddOdometerRequest request) =>
        Ok(ApiResponse<OdometerModel>.Ok(await odometer.AddAsync(id, request, Caller), "Reading saved."));
}

/// <summary>The vehicles a partner has to do with, for the partner screen's Linked Vehicles tab (FSD §9.2).</summary>
[ApiController]
[Route("api/partners/{partnerId:int}/vehicles")]
public class PartnerVehiclesController(ILinkedVehiclesService linked) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> List(int partnerId) => Ok(ApiResponse<List<LinkedVehicle>>.Ok(await linked.ForPartnerAsync(partnerId)));
}