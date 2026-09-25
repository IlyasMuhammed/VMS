using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§37/§47.3's two payment-entry routes — one single-invoice ("Record Payment" opens with the invoice
/// pre-selected), one multi-invoice (a receipt allocated across several) — both backed by the same
/// <see cref="IPaymentReceiptService.CreateAsync"/>, since the common single-invoice case is just a one-item
/// allocation list. Both carry the flat <c>Payment.Create</c> gate; "Settle remaining balance" needs the
/// stricter, conditional `Payment.WriteOff`/`Payment.Discount` on top of that, checked here rather than as a
/// second flat attribute, since it only applies when the request actually asks for it.</summary>
[ApiController]
public sealed class PaymentReceiptController(IPaymentReceiptService receipts, IInvoiceCreationService invoices, IInvoicePaymentTransferService transfers) : ControllerBase
{
    [HttpPost("api/invoices/{invoiceId:long}/payments")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_CREATE)]
    public async Task<IActionResult> RecordAgainstInvoice(long invoiceId, [FromBody] RecordInvoicePaymentRequest request)
    {
        EnsureSettlementPermission(request.SettleRemaining);
        var invoice = await invoices.GetAsync(invoiceId);

        var full = new CreatePaymentReceiptRequest
        {
            CustomerId = invoice.CustomerId, ReceiptDate = request.ReceiptDate, ReceiptAmount = request.Amount, PaymentMethod = request.PaymentMethod,
            BankCashAccountId = request.BankCashAccountId, InstrumentNo = request.InstrumentNo, InstrumentDate = request.InstrumentDate,
            DrawnOnBank = request.DrawnOnBank, PaymentReference = request.PaymentReference, AttachmentDocumentId = request.AttachmentId,
            Remarks = request.Remarks, ConfirmOverpayment = request.ConfirmOverpayment, ConfirmDuplicate = request.ConfirmDuplicate,
            Allocations = [new PaymentAllocationRequest { InvoiceId = invoiceId, Amount = request.Amount }], SettleRemaining = request.SettleRemaining
        };
        return Ok(ApiResponse<CustomerReceiptModel>.Ok(await receipts.CreateAsync(full, User.GetUserId()), "Payment recorded."));
    }

    [HttpPost("api/customer-receipts")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_CREATE)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentReceiptRequest request)
    {
        EnsureSettlementPermission(request.SettleRemaining);
        return Ok(ApiResponse<CustomerReceiptModel>.Ok(await receipts.CreateAsync(request, User.GetUserId()), "Receipt recorded."));
    }

    /// <summary>§47.2: "GET /api/invoices/{id}/payments · /payment-transfers → History → Invoice.View."</summary>
    [HttpGet("api/invoices/{invoiceId:long}/payment-transfers")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> ListTransfers(long invoiceId) =>
        Ok(ApiResponse<IReadOnlyList<PaymentTransferModel>>.Ok(await transfers.ListForInvoiceAsync(invoiceId), "Payment transfers."));

    private void EnsureSettlementPermission(SettleRemainingRequest? settleRemaining)
    {
        if (settleRemaining is null) return;
        var needed = settleRemaining.SettlementType == SettlementTypes.WriteOff ? PermissionCodes.TRP_PAYMENT_WRITEOFF : PermissionCodes.TRP_PAYMENT_DISCOUNT;
        if (User.IsSuperAdmin()) return;
        if (User.Claims.Any(c => c.Type == "permission" && c.Value == needed)) return;
        throw new ForbiddenException($"Settling the remaining balance needs the {needed} permission.");
    }
}

/// <summary>§47.3's own literal body shape for the single-invoice route — no <c>customerId</c> (derived from the
/// invoice) and no <c>allocations</c> (always exactly this one).</summary>
public sealed class RecordInvoicePaymentRequest
{
    public DateOnly ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public DateOnly? InstrumentDate { get; set; }
    public string? DrawnOnBank { get; set; }
    public string? PaymentReference { get; set; }
    public long? AttachmentId { get; set; }
    public string? Remarks { get; set; }
    public bool ConfirmOverpayment { get; set; }
    public bool ConfirmDuplicate { get; set; }
    public SettleRemainingRequest? SettleRemaining { get; set; }
}
