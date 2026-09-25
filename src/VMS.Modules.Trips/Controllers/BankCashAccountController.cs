using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§37: the company's own bank accounts. View — whoever can record a payment (they need the list to
/// pick from); create/update — Admin/Finance (`TRP.BANKACCOUNT.MANAGE`).</summary>
[ApiController]
[Route("api/bank-accounts")]
public sealed class BankCashAccountController(IBankCashAccountService accounts) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_CREATE)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<BankCashAccountModel>>.Ok(await accounts.ListAsync(includeInactive)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_BANKACCOUNT_MANAGE)]
    public async Task<IActionResult> Create([FromBody] SaveBankCashAccountRequest request) =>
        Ok(ApiResponse<BankCashAccountModel>.Ok(await accounts.CreateAsync(request), "Bank account created."));

    [HttpPut("{bankCashAccountId:long}")]
    [RequirePermission(PermissionCodes.TRP_BANKACCOUNT_MANAGE)]
    public async Task<IActionResult> Update(long bankCashAccountId, [FromBody] SaveBankCashAccountRequest request) =>
        Ok(ApiResponse<BankCashAccountModel>.Ok(await accounts.UpdateAsync(bankCashAccountId, request), "Bank account updated."));
}
