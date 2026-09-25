using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§37.5, AC-61: settle part of a Submitted invoice as a Write-off or a Discount, standalone (Invoice
/// Detail). `Payment.WriteOff`/`Payment.Discount` are two separate permissions for the same action depending on
/// <c>SettlementType</c> — a genuinely conditional gate, so <c>[AuthenticatedOnly]</c> rather than one flat
/// <c>[RequirePermission]</c>, the same shape <c>PaymentReceiptController</c>'s own "Settle remaining balance"
/// embedding already uses.</summary>
[ApiController]
public sealed class InvoiceSettlementController(IInvoiceSettlementService settlements) : ControllerBase
{
    [HttpPost("api/invoices/{invoiceId:long}/settlements")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Create(long invoiceId, [FromBody] CreateSettlementRequest request)
    {
        var needed = request.SettlementType == SettlementTypes.WriteOff ? PermissionCodes.TRP_PAYMENT_WRITEOFF : PermissionCodes.TRP_PAYMENT_DISCOUNT;
        if (!Has(needed)) throw new ForbiddenException($"This action needs the {needed} permission.");
        return Ok(ApiResponse<InvoiceSettlementModel>.Ok(await settlements.CreateAsync(invoiceId, request, User.GetUserId()), "Settlement recorded."));
    }

    [HttpPost("api/invoice-settlements/{invoiceSettlementId:long}/reverse")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Reverse(long invoiceSettlementId, [FromBody] ReverseSettlementRequest request)
    {
        // Reversing either type is gated the same coarse way §47.2's own API table already groups these four
        // credit-handling actions (CarryForward/Refund/WriteOff/Discount) under one shared permission list,
        // rather than needing a read of the settlement's own type before the permission check can run.
        if (!Has(PermissionCodes.TRP_PAYMENT_WRITEOFF) && !Has(PermissionCodes.TRP_PAYMENT_DISCOUNT))
            throw new ForbiddenException($"This action needs the {PermissionCodes.TRP_PAYMENT_WRITEOFF} or {PermissionCodes.TRP_PAYMENT_DISCOUNT} permission.");
        return Ok(ApiResponse<InvoiceSettlementModel>.Ok(await settlements.ReverseAsync(invoiceSettlementId, request, User.GetUserId()), "Settlement reversed."));
    }

    private bool Has(string permission) => User.IsSuperAdmin() || User.Claims.Any(c => c.Type == "permission" && c.Value == permission);
}
