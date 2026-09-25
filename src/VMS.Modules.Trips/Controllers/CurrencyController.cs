using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Currency master and exchange rates (FSD §13A). Both need <see cref="PermissionCodes.TRP_EXCHANGERATE_MANAGE"/>
/// or <see cref="PermissionCodes.TRP_CURRENCY_MANAGE"/> as §44 splits them: Admin holds both by default; Finance can be
/// granted exchange-rate maintenance on its own without the currency master/tenant setting.</summary>
[ApiController]
[Route("api")]
public sealed class CurrencyController(ICurrencyService currencies) : ControllerBase
{
    [HttpGet("currencies")]
    [RequirePermission(PermissionCodes.TRP_CURRENCY_MANAGE)]
    public async Task<IActionResult> List() => Ok(ApiResponse<IReadOnlyList<CurrencyModel>>.Ok(await currencies.ListAsync()));

    [HttpPost("currencies")]
    [RequirePermission(PermissionCodes.TRP_CURRENCY_MANAGE)]
    public async Task<IActionResult> Create([FromBody] SaveCurrencyRequest request) =>
        Ok(ApiResponse<CurrencyModel>.Ok(await currencies.CreateAsync(request)));

    [HttpPut("currencies/{currencyCode}")]
    [RequirePermission(PermissionCodes.TRP_CURRENCY_MANAGE)]
    public async Task<IActionResult> Update(string currencyCode, [FromBody] SaveCurrencyRequest request) =>
        Ok(ApiResponse<CurrencyModel>.Ok(await currencies.UpdateAsync(currencyCode, request)));

    [HttpGet("tenant/currency-settings")]
    [RequirePermission(PermissionCodes.TRP_CURRENCY_MANAGE)]
    public async Task<IActionResult> GetSettings() => Ok(ApiResponse<CurrencySettingsModel>.Ok(await currencies.GetSettingsAsync()));

    [HttpPut("tenant/currency-settings")]
    [RequirePermission(PermissionCodes.TRP_CURRENCY_MANAGE)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateCurrencySettingsRequest request) =>
        Ok(ApiResponse<CurrencySettingsModel>.Ok(await currencies.UpdateSettingsAsync(request)));

    [HttpGet("exchange-rates")]
    [RequirePermission(PermissionCodes.TRP_EXCHANGERATE_MANAGE)]
    public async Task<IActionResult> ListExchangeRates() => Ok(ApiResponse<IReadOnlyList<ExchangeRateModel>>.Ok(await currencies.ListExchangeRatesAsync()));

    [HttpPost("exchange-rates")]
    [RequirePermission(PermissionCodes.TRP_EXCHANGERATE_MANAGE)]
    public async Task<IActionResult> CreateExchangeRate([FromBody] SaveExchangeRateRequest request) =>
        Ok(ApiResponse<ExchangeRateModel>.Ok(await currencies.CreateExchangeRateAsync(request)));
}
