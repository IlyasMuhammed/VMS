namespace VMS.Shared.Branches;

/// <summary>What another module needs to know about a branch it points at.</summary>
public sealed record BranchInfo(Guid Id, string Code, string Name, bool IsActive)
{
    /// <summary>Can be picked for something new. A retired branch stays valid on the records that already point at it.</summary>
    public bool IsAvailable => IsActive;
}

/// <summary>
/// Looks branches up for the modules that point at one (partners, vehicles, from Stage 6 users), so those modules never read
/// the branch table themselves. Only the caller's own tenant's branches are visible; anything else is simply not found.
/// </summary>
public interface IBranchDirectory
{
    Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>The branches found, by id, for naming a page of list rows. An id that is not found (or belongs to another tenant) is left out.</summary>
    Task<IReadOnlyDictionary<Guid, BranchInfo>> FindManyAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default);
}
