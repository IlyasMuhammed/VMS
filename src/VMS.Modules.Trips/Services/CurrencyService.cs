using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

public interface ICurrencyService
{
    Task<IReadOnlyList<CurrencyModel>> ListAsync(CancellationToken ct = default);
    Task<CurrencyModel> CreateAsync(SaveCurrencyRequest request, CancellationToken ct = default);
    Task<CurrencyModel> UpdateAsync(string currencyCode, SaveCurrencyRequest request, CancellationToken ct = default);
    Task<CurrencySettingsModel> GetSettingsAsync(CancellationToken ct = default);
    Task<CurrencySettingsModel> UpdateSettingsAsync(UpdateCurrencySettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeRateModel>> ListExchangeRatesAsync(CancellationToken ct = default);
    Task<ExchangeRateModel> CreateExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken ct = default);
}

internal sealed partial class CurrencyService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ICurrencySeeder seeder) : ICurrencyService
{
    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<CurrencyModel>> ListAsync(CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        return await db.Currencies.AsNoTracking().OrderByDescending(c => c.IsBase).ThenBy(c => c.CurrencyCode)
            .Select(c => ToModel(c)).ToListAsync(ct);
    }

    public async Task<CurrencyModel> CreateAsync(SaveCurrencyRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var code = (request.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code)) Add("currencyCode", Msg.TrpCurrencyCodeInvalid, ("Field", "Currency code"));
        if (string.IsNullOrWhiteSpace(request.CurrencyName)) Add("currencyName", Msg.Required, ("Field", "Currency name"));
        if (request.Status is not (CurrencyStatuses.Active or CurrencyStatuses.Inactive))
            Add("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", CurrencyStatuses.All)));

        if (errors.Count == 0 && await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == code, ct))
            Add("currencyCode", Msg.TrpCurrencyCodeUsed, ("Code", code));
        if (errors.Count > 0) throw new ValidationException(errors);

        var currency = new Currency
        {
            CurrencyCode = code, CurrencyName = request.CurrencyName.Trim(),
            Symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim(),
            DecimalPlaces = request.DecimalPlaces == 0 ? (byte)2 : request.DecimalPlaces,
            Status = request.Status, IsBase = false
        };
        db.Currencies.Add(currency);
        await db.SaveChangesAsync(ct);
        return ToModel(currency);
    }

    public async Task<CurrencyModel> UpdateAsync(string currencyCode, SaveCurrencyRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var code = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var currency = await db.Currencies.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == code, ct)
            ?? throw new NotFoundException($"Currency '{code}' was not found.");

        var errors = new List<ValidationError>();
        void Add(string field, string msgCode, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, msgCode, values));
        if (string.IsNullOrWhiteSpace(request.CurrencyName)) Add("currencyName", Msg.Required, ("Field", "Currency name"));
        if (request.Status is not (CurrencyStatuses.Active or CurrencyStatuses.Inactive))
            Add("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", CurrencyStatuses.All)));
        if (currency.IsBase && request.Status == CurrencyStatuses.Inactive) Add("status", Msg.TrpBaseCurrencyInactive, ("Code", code));
        if (errors.Count > 0) throw new ValidationException(errors);

        currency.CurrencyName = request.CurrencyName.Trim();
        currency.Symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim();
        currency.DecimalPlaces = request.DecimalPlaces == 0 ? (byte)2 : request.DecimalPlaces;
        currency.Status = request.Status;
        await db.SaveChangesAsync(ct);
        return ToModel(currency);
    }

    public async Task<CurrencySettingsModel> GetSettingsAsync(CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var settings = await db.CurrencySettings.AsNoTracking().FirstAsync(s => s.TenantId == tenant.TenantId, ct);
        return new CurrencySettingsModel { BaseCurrencyCode = settings.BaseCurrencyCode, MultiCurrencyEnabled = settings.MultiCurrencyEnabled };
    }

    public async Task<CurrencySettingsModel> UpdateSettingsAsync(UpdateCurrencySettingsRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var settings = await db.CurrencySettings.FirstAsync(s => s.TenantId == tenant.TenantId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var newBase = string.IsNullOrWhiteSpace(request.BaseCurrencyCode) ? settings.BaseCurrencyCode : request.BaseCurrencyCode.Trim().ToUpperInvariant();
        Currency? newBaseCurrency = null;
        if (newBase != settings.BaseCurrencyCode)
        {
            newBaseCurrency = await db.Currencies.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == newBase, ct);
            if (newBaseCurrency is null) Add("baseCurrencyCode", Msg.TrpBaseCurrencyUnknown, ("Code", newBase));
            else if (newBaseCurrency.Status != CurrencyStatuses.Active) Add("baseCurrencyCode", Msg.TrpBaseCurrencyInactive, ("Code", newBase));
            // §13A: "The base currency cannot be changed after transactions exist." No transaction-carrying entity
            // exists in this module yet (Trip/Invoice/etc. land in later CC tasks) — nothing to check against today.
            // Extend this guard with a real query once one of them stamps CurrencyCode/BaseAmount at entry.
        }
        if (settings.MultiCurrencyEnabled && !request.MultiCurrencyEnabled)
        {
            // §13A: "Turning multi-currency off is blocked while any non-base-currency transaction is open." Same
            // reason as above — nothing to check yet; extend once a currency-carrying transaction table exists.
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        if (newBaseCurrency is not null)
        {
            var oldBase = await db.Currencies.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.IsBase, ct);
            if (oldBase is not null) oldBase.IsBase = false;
            newBaseCurrency.IsBase = true;
            settings.BaseCurrencyCode = newBase;
        }
        settings.MultiCurrencyEnabled = request.MultiCurrencyEnabled;
        await db.SaveChangesAsync(ct);
        return new CurrencySettingsModel { BaseCurrencyCode = settings.BaseCurrencyCode, MultiCurrencyEnabled = settings.MultiCurrencyEnabled };
    }

    public async Task<IReadOnlyList<ExchangeRateModel>> ListExchangeRatesAsync(CancellationToken ct = default) =>
        await db.ExchangeRates.AsNoTracking().OrderByDescending(r => r.RateDate).ThenBy(r => r.FromCurrencyCode)
            .Select(r => ToModel(r)).ToListAsync(ct);

    public async Task<ExchangeRateModel> CreateExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var settings = await db.CurrencySettings.AsNoTracking().FirstAsync(s => s.TenantId == tenant.TenantId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var from = (request.FromCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(from) || !CodePattern().IsMatch(from)) Add("fromCurrencyCode", Msg.TrpCurrencyCodeInvalid, ("Field", "From currency"));
        else if (!await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == from, ct))
            Add("fromCurrencyCode", Msg.TrpExchangeRateCurrencyUnknown, ("Code", from));
        else if (from == settings.BaseCurrencyCode) Add("fromCurrencyCode", Msg.TrpExchangeRateToMustBeBase, ("Base", settings.BaseCurrencyCode));

        if (request.RateDate is null) Add("rateDate", Msg.Required, ("Field", "Rate date"));
        if (request.Rate is null or <= 0) Add("rate", Msg.Min, ("Field", "Rate"), ("Min", "0.000001"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var rateDate = request.RateDate!.Value;
        if (await db.ExchangeRates.AnyAsync(r => r.TenantId == tenant.TenantId && r.FromCurrencyCode == from && r.ToCurrencyCode == settings.BaseCurrencyCode && r.RateDate == rateDate, ct))
            throw new ValidationException(messages.Error("rateDate", Msg.TrpExchangeRateDuplicate, ("From", from), ("To", settings.BaseCurrencyCode), ("RateDate", rateDate.ToString("yyyy-MM-dd"))));

        var rate = new ExchangeRate { FromCurrencyCode = from, ToCurrencyCode = settings.BaseCurrencyCode, RateDate = rateDate, Rate = request.Rate!.Value };
        db.ExchangeRates.Add(rate);
        await db.SaveChangesAsync(ct);
        return ToModel(rate);
    }

    private static CurrencyModel ToModel(Currency c) => new()
    {
        CurrencyId = c.CurrencyId, CurrencyCode = c.CurrencyCode, CurrencyName = c.CurrencyName,
        Symbol = c.Symbol, DecimalPlaces = c.DecimalPlaces, IsBase = c.IsBase, Status = c.Status
    };

    private static ExchangeRateModel ToModel(ExchangeRate r) => new()
    {
        ExchangeRateId = r.ExchangeRateId, FromCurrencyCode = r.FromCurrencyCode, ToCurrencyCode = r.ToCurrencyCode,
        RateDate = r.RateDate, Rate = r.Rate
    };
}
