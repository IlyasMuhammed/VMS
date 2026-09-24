namespace VMS.Shared.Partners;

/// <summary>What another module needs to know about a partner it points at: who it is, whether it can still be chosen, and its roles.</summary>
public sealed record PartnerInfo(int Id, string BpCode, string LegalName, string DisplayName, string Status, IReadOnlyList<string> Roles)
{
    /// <summary>Can be picked for something new: Active only, never Inactive, Blacklisted or Merged (BR-BP-020, BR-VH-020).</summary>
    public bool IsAvailable => Status == "Active";

    public bool HasRole(string role) => Roles.Contains(role);
}

/// <summary>
/// Looks partners up for the modules that point at them (vehicles, finance, trips), so those modules never read the partner
/// tables. Only the caller's own tenant's partners are visible; anything else is simply not found.
/// </summary>
public interface IPartnerDirectory
{
    Task<PartnerInfo?> FindAsync(int partnerId, CancellationToken cancellationToken = default);

    /// <summary>The partners found, by id. An id that is not found (or belongs to another tenant) is left out.</summary>
    Task<IReadOnlyDictionary<int, PartnerInfo>> FindManyAsync(IEnumerable<int> partnerIds, CancellationToken cancellationToken = default);

    /// <summary>Every partner of the tenant, for a report that must consider all of them (the Missing Documents report). Phase 1's NFR budget (under 10,000 partners) keeps a full scan inside its 2-second target.</summary>
    Task<IReadOnlyList<PartnerInfo>> AllAsync(CancellationToken cancellationToken = default);
}
