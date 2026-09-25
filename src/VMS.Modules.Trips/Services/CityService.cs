using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

public interface ICityService
{
    Task<IReadOnlyList<CityModel>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<CityModel> CreateAsync(SaveCityRequest request, CancellationToken ct = default);
    Task<CityModel> UpdateAsync(int cityId, SaveCityRequest request, CancellationToken ct = default);
}

internal sealed partial class CityService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ILookupReader lookups, ICitySeeder seeder) : ICityService
{
    [GeneratedRegex("^[A-Z]{2,5}$")]
    private static partial Regex AbbreviationPattern();

    public async Task<IReadOnlyList<CityModel>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var query = db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId);
        if (!includeInactive) query = query.Where(c => c.Status == ActiveInactiveStatuses.Active);
        return await query.OrderBy(c => c.CityName).Select(c => ToModel(c)).ToListAsync(ct);
    }

    public async Task<CityModel> CreateAsync(SaveCityRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var (errors, countryId) = await ValidateAsync(request, existingId: null, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        var city = new City
        {
            CityName = request.CityName.Trim(), Abbreviation = request.Abbreviation.Trim().ToUpperInvariant(),
            CountryId = countryId, ProvinceState = string.IsNullOrWhiteSpace(request.ProvinceState) ? null : request.ProvinceState.Trim(),
            Status = request.Status
        };
        db.Cities.Add(city);
        await db.SaveChangesAsync(ct);
        return ToModel(city);
    }

    public async Task<CityModel> UpdateAsync(int cityId, SaveCityRequest request, CancellationToken ct = default)
    {
        await seeder.EnsureSeededAsync(ct);
        var city = await db.Cities.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CityId == cityId, ct)
            ?? throw new NotFoundException($"City {cityId} was not found.");

        var (errors, countryId) = await ValidateAsync(request, cityId, ct);

        // §16: "Abbreviation is immutable once used in a Route or TripCode" (CC-09) — a route code like RT-LHR-FSD
        // embeds it directly, so changing it after the fact would silently mismatch every route built from it.
        var newAbbreviation = (request.Abbreviation ?? string.Empty).Trim().ToUpperInvariant();
        if (newAbbreviation != city.Abbreviation && await db.RouteStops.AnyAsync(s => s.TenantId == tenant.TenantId && s.CityId == cityId, ct))
            errors.Add(messages.Error("abbreviation", Msg.ReadOnlyOnceSaved, ("Field", "Abbreviation")));
        if (errors.Count > 0) throw new ValidationException(errors);

        city.CityName = request.CityName.Trim();
        city.Abbreviation = newAbbreviation;
        city.CountryId = countryId;
        city.ProvinceState = string.IsNullOrWhiteSpace(request.ProvinceState) ? null : request.ProvinceState.Trim();
        city.Status = request.Status;
        await db.SaveChangesAsync(ct);
        return ToModel(city);
    }

    private async Task<(List<ValidationError> Errors, int CountryId)> ValidateAsync(SaveCityRequest request, int? existingId, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.CityName)) Add("cityName", Msg.Required, ("Field", "City name"));
        var abbreviation = (request.Abbreviation ?? string.Empty).Trim().ToUpperInvariant();
        if (!AbbreviationPattern().IsMatch(abbreviation)) Add("abbreviation", Msg.Format, ("Field", "Abbreviation"));
        else if (await db.Cities.AnyAsync(c => c.TenantId == tenant.TenantId && c.Abbreviation == abbreviation && c.CityId != (existingId ?? 0), ct))
            Add("abbreviation", Msg.AlreadyUsed, ("Field", "abbreviation"), ("Code", abbreviation), ("Name", "another city"));
        if (request.Status is not (ActiveInactiveStatuses.Active or ActiveInactiveStatuses.Inactive))
            Add("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", ActiveInactiveStatuses.All)));

        int countryId;
        if (request.CountryId is { } given)
        {
            if (await lookups.FindAsync(PlatformLookups.Country, given) is null) Add("countryId", Msg.Invalid, ("Field", "Country"));
            countryId = given;
        }
        else
        {
            var pakistan = (await lookups.GetActiveAsync(PlatformLookups.Country)).FirstOrDefault(c => c.Code == "PAKISTAN");
            if (pakistan is null) { Add("countryId", Msg.Required, ("Field", "Country")); countryId = 0; }
            else countryId = pakistan.Id;
        }

        if (errors.Count == 0)
        {
            var name = request.CityName.Trim();
            var province = string.IsNullOrWhiteSpace(request.ProvinceState) ? null : request.ProvinceState.Trim();
            if (await db.Cities.AnyAsync(c => c.TenantId == tenant.TenantId && c.CountryId == countryId && c.ProvinceState == province
                    && c.CityName == name && c.CityId != (existingId ?? 0), ct))
                Add("cityName", Msg.AlreadyUsed, ("Field", "city name"), ("Code", name), ("Name", "another city in this province"));
        }

        return (errors, countryId);
    }

    private static CityModel ToModel(City c) => new()
    {
        CityId = c.CityId, CityName = c.CityName, Abbreviation = c.Abbreviation, CountryId = c.CountryId,
        ProvinceState = c.ProvinceState, Status = c.Status
    };
}
