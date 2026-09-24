using Microsoft.EntityFrameworkCore;
using VMS.Modules.BusinessPartners.Data;
using VMS.Modules.BusinessPartners.Domain;
using VMS.Modules.BusinessPartners.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;

namespace VMS.Modules.BusinessPartners.Services;

/// <summary>
/// Finds partners that look like the one being entered (FSD §12.1), and reports the ones that make a save impossible
/// (a CNIC or NTN already used) or need the person's say-so (a shared mobile, the same name in the same city, a name
/// that is 85% alike). The screen calls it while typing; the save calls it again, so nothing depends on the screen having asked.
/// </summary>
internal sealed class DuplicateService(PartnerDbContext db, ITenantContext tenantContext, ILookupReader lookups, IMessageCatalogue messages)
{
    /// <summary>Names this alike are shown as possible duplicates (FSD §12.1).</summary>
    public const double SimilarityThreshold = 0.85;

    private Guid Tenant => tenantContext.TenantId;

    /// <summary>Partners that still count: not merged into another, not soft-deleted (BR-BP-013, BR-BP-015).</summary>
    private IQueryable<BusinessPartner> Live() =>
        db.Partners.AsNoTracking().Where(p => p.TenantId == Tenant && p.Status != PartnerStatuses.Merged && !p.IsDeleted);

    private static readonly IReadOnlyDictionary<string, int> Priority = new Dictionary<string, int>
    {
        ["Cnic"] = 0, ["Ntn"] = 1, ["NameAndCity"] = 2, ["Mobile"] = 3, ["NameSimilar"] = 4
    };

    public async Task<List<DuplicateMatch>> FindAsync(DuplicateCheckRequest q, CancellationToken ct = default)
    {
        var excludeId = q.ExcludeId ?? 0;
        var found = new Dictionary<int, (string Type, double? Score)>();

        void Note(int id, string type, double? score = null)
        {
            if (!found.TryGetValue(id, out var existing) || Priority[type] < Priority[existing.Type]) found[id] = (type, score);
        }

        var cnic = q.Cnic?.Trim();
        if (!string.IsNullOrEmpty(cnic))
            foreach (var id in await Live().Where(p => p.Cnic == cnic && p.BusinessPartnerId != excludeId).Select(p => p.BusinessPartnerId).ToListAsync(ct))
                Note(id, "Cnic");

        var ntn = q.Ntn?.Trim();
        if (!string.IsNullOrEmpty(ntn) && q.PartyType != PartyTypes.Person)
            foreach (var id in await Live().Where(p => p.Ntn == ntn && p.PartyType == PartyTypes.Company && p.BusinessPartnerId != excludeId).Select(p => p.BusinessPartnerId).ToListAsync(ct))
                Note(id, "Ntn");

        var mobile = PartnerFormats.NormalizeMobile(q.PrimaryMobile);
        if (mobile is not null)
            foreach (var id in await Live().Where(p => p.PrimaryMobile == mobile && p.BusinessPartnerId != excludeId).Select(p => p.BusinessPartnerId).ToListAsync(ct))
                Note(id, "Mobile");

        var name = q.LegalName?.Trim();
        if (!string.IsNullOrEmpty(name) && name.Length >= 3)
        {
            var normalised = NameSimilarity.Normalize(name);
            var names = await Live().Where(p => p.BusinessPartnerId != excludeId)
                .Select(p => new { p.BusinessPartnerId, p.LegalName, p.CityId }).ToListAsync(ct);

            foreach (var n in names)
            {
                var sameName = string.Equals(NameSimilarity.Normalize(n.LegalName), normalised, StringComparison.Ordinal);
                if (sameName && q.CityId is { } city && n.CityId == city) { Note(n.BusinessPartnerId, "NameAndCity", 1); continue; }

                var score = sameName ? 1 : NameSimilarity.Score(name, n.LegalName);
                if (score >= SimilarityThreshold) Note(n.BusinessPartnerId, "NameSimilar", Math.Round(score, 2));
            }
        }

        return await DescribeAsync(found, q.CityId, ct);
    }

    private async Task<List<DuplicateMatch>> DescribeAsync(Dictionary<int, (string Type, double? Score)> found, int? cityId, CancellationToken ct)
    {
        if (found.Count == 0) return [];

        var ids = found.Keys.ToList();
        var rows = await db.Partners.AsNoTracking().Where(p => ids.Contains(p.BusinessPartnerId))
            .Select(p => new { p.BusinessPartnerId, p.BpCode, p.LegalName, p.CityId, p.Status }).ToListAsync(ct);
        var roles = (await db.Roles.AsNoTracking().Where(r => ids.Contains(r.BusinessPartnerId) && r.IsActive)
            .Select(r => new { r.BusinessPartnerId, r.RoleCode }).ToListAsync(ct)).ToLookup(r => r.BusinessPartnerId, r => r.RoleCode);
        var cities = await lookups.FindManyAsync(PlatformLookups.City, rows.Select(r => r.CityId));

        return rows
            .Select(r => new DuplicateMatch
            {
                Id = r.BusinessPartnerId,
                BpCode = r.BpCode,
                LegalName = r.LegalName,
                Roles = roles[r.BusinessPartnerId].ToList(),
                CityId = r.CityId,
                City = cities.GetValueOrDefault(r.CityId)?.Description,
                Status = r.Status,
                MatchType = found[r.BusinessPartnerId].Type,
                IsHard = found[r.BusinessPartnerId].Type is "Cnic" or "Ntn",
                Score = found[r.BusinessPartnerId].Score,
                SameCity = cityId is { } c && r.CityId == c
            })
            .OrderBy(m => Priority[m.MatchType]).ThenByDescending(m => m.Score ?? 0).ThenBy(m => m.BpCode)
            .ToList();
    }

    // ── For a save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// What stops a save outright: a CNIC or NTN another live partner already has (BR-BP-013), an STRN in use, a
    /// driver licence already held by another active driver. Each says which partner has it, so the user can open that record instead.
    /// </summary>
    public async Task<List<ValidationError>> UniquenessErrorsAsync(PartnerInput input, int? excludeId, IReadOnlyCollection<string> activeRoles, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        var exclude = excludeId ?? 0;

        var hard = await FindAsync(new DuplicateCheckRequest { Cnic = input.Cnic, Ntn = input.Ntn, PartyType = input.PartyType, ExcludeId = excludeId }, ct);
        if (hard.FirstOrDefault(m => m.MatchType == "Cnic") is { } byCnic)
            errors.Add(messages.Error("cnic", Msg.BpCnicUsed, ("BPCode", byCnic.BpCode), ("LegalName", byCnic.LegalName)));
        if (hard.FirstOrDefault(m => m.MatchType == "Ntn") is { } byNtn)
            errors.Add(messages.Error("ntn", Msg.BpNtnUsed, ("BPCode", byNtn.BpCode), ("LegalName", byNtn.LegalName)));

        if (input.Strn is { } strn)
        {
            var other = await db.Partners.AsNoTracking()
                .Where(p => p.TenantId == Tenant && !p.IsDeleted && p.Strn == strn && p.BusinessPartnerId != exclude)
                .Select(p => new { p.BpCode, p.LegalName }).FirstOrDefaultAsync(ct);
            if (other is not null) errors.Add(messages.Error("strn", Msg.AlreadyUsed, ("Field", "STRN"), ("Code", other.BpCode), ("Name", other.LegalName)));
        }

        if (activeRoles.Contains(PartnerRoles.Driver) && input.Driver?.LicenceNo is { Length: > 0 } licence)
        {
            var holder = await LicenceHolderAsync(licence, exclude, ct);
            if (holder is not null) errors.Add(messages.Error("driver.licenceNo", Msg.AlreadyUsed, ("Field", "licence number"), ("Code", holder.Value.BpCode), ("Name", holder.Value.LegalName)));
        }
        return errors;
    }

    /// <summary>The other active driver who holds this licence number (FSD §7.1: unique among active drivers), if any.</summary>
    public async Task<(string BpCode, string LegalName)?> LicenceHolderAsync(string licenceNo, int excludeId, CancellationToken ct = default)
    {
        var holder = await (from d in db.DriverDetails.AsNoTracking()
                            join p in db.Partners.AsNoTracking() on d.BusinessPartnerId equals p.BusinessPartnerId
                            where d.TenantId == Tenant && d.LicenceNo == licenceNo && p.BusinessPartnerId != excludeId
                                  && !p.IsDeleted && p.Status != PartnerStatuses.Merged
                                  && db.Roles.Any(r => r.BusinessPartnerId == p.BusinessPartnerId && r.RoleCode == PartnerRoles.Driver && r.IsActive)
                            select new { p.BpCode, p.LegalName }).FirstOrDefaultAsync(ct);
        return holder is null ? null : (holder.BpCode, holder.LegalName);
    }

    /// <summary>
    /// The soft duplicates that need the person's say-so before saving: a shared mobile, the same name in the same
    /// city, or a similar name in the same city. A similar name in another city is shown by the check but not asked about.
    /// </summary>
    public async Task<List<DuplicateMatch>> SoftMatchesAsync(PartnerInput input, int? excludeId, CancellationToken ct = default)
    {
        var matches = await FindAsync(new DuplicateCheckRequest
        {
            LegalName = input.LegalName, PrimaryMobile = input.PrimaryMobile, CityId = input.CityId, ExcludeId = excludeId
        }, ct);
        return matches.Where(m => !m.IsHard && (m.MatchType != "NameSimilar" || m.SameCity)).ToList();
    }

    /// <summary>The errors for soft duplicates the person has not acknowledged; empty if they named every one.</summary>
    public async Task<(List<ValidationError> Errors, List<DuplicateMatch> Acknowledged)> CheckAcknowledgementAsync(PartnerInput input, int? excludeId, CancellationToken ct = default)
    {
        var soft = await SoftMatchesAsync(input, excludeId, ct);
        var acknowledged = soft.Where(m => input.AcknowledgedDuplicateIds.Contains(m.Id)).ToList();
        var pending = soft.Except(acknowledged).ToList();

        var errors = new List<ValidationError>();
        var byName = pending.Where(m => m.MatchType is "NameAndCity" or "NameSimilar").ToList();
        if (byName.Count > 0)
        {
            var city = (await lookups.FindAsync(PlatformLookups.City, input.CityId))?.Description ?? string.Empty;
            errors.Add(messages.Error("legalName", Msg.BpSimilarName, ("n", byName.Count), ("City", city)));
        }
        var byMobile = pending.Where(m => m.MatchType == "Mobile").ToList();
        if (byMobile.Count > 0)
            errors.Add(messages.Error("primaryMobile", Msg.OthersHaveThis, ("n", byMobile.Count), ("Field", "mobile number")));

        return (errors, acknowledged);
    }
}
