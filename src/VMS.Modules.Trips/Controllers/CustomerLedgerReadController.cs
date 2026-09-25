using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§40A.4, §47.2: the three ledger read screens (statement, invoice ledger, balances) plus the
/// statement's own PDF export. All `Ledger.View` (Admin, Finance, Read Only — Fleet/Operations no access,
/// §40A's own permissions table).</summary>
[ApiController]
public sealed class CustomerLedgerReadController(ICustomerLedgerReadService ledger) : ControllerBase
{
    [HttpGet("api/customers/{customerId:int}/ledger")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> Statement(int customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] long? invoiceId, [FromQuery] string? currencyCode, [FromQuery] bool includeReversedPairs = true) =>
        Ok(ApiResponse<CustomerLedgerStatementModel>.Ok(await ledger.GetStatementAsync(customerId, from, to, invoiceId, currencyCode, includeReversedPairs)));

    [HttpGet("api/customers/{customerId:int}/ledger/statement.pdf")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> StatementPdf(int customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? currencyCode)
    {
        var (bytes, fileName) = await ledger.ExportStatementPdfAsync(customerId, from, to, currencyCode);
        return File(bytes, "application/pdf", fileName);
    }

    [HttpGet("api/invoices/{invoiceId:long}/ledger")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> InvoiceLedger(long invoiceId) =>
        Ok(ApiResponse<InvoiceLedgerModel>.Ok(await ledger.GetInvoiceLedgerAsync(invoiceId)));

    [HttpGet("api/customers/{customerId:int}/balance")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> CustomerBalance(int customerId) =>
        Ok(ApiResponse<IReadOnlyList<CustomerBalanceModel>>.Ok(await ledger.GetCustomerBalancesAsync(customerId)));

    [HttpGet("api/customer-balances")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> AllBalances() =>
        Ok(ApiResponse<IReadOnlyList<CustomerBalanceSummaryModel>>.Ok(await ledger.ListBalancesAsync()));
}
