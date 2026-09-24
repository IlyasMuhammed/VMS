namespace VMS.Shared.Partners;

/// <summary>What a business partner is being used for, so the partner module can refuse a change that would strand it.</summary>
public enum PartnerIntent
{
    /// <summary>Removing a role from the partner (BR-BP-011).</summary>
    RemoveRole,
    /// <summary>Setting the partner Inactive (BR-BP-014).</summary>
    Deactivate,
    /// <summary>Changing Party Type (BR-BP-012).</summary>
    ChangePartyType,
}

/// <summary>One record that depends on the partner: "Driver assigned to vehicle LEA-1234".</summary>
/// <param name="Kind">What sort of record, for grouping: <c>Vehicle</c>, <c>FinanceAgreement</c>, …</param>
/// <param name="RecordCode">The record's own code or number, for the "View list" link.</param>
public sealed record PartnerUsage(string Kind, string RecordCode, string Description);

/// <summary>
/// Implemented by every module that points at a business partner (vehicles, finance, trips, documents). The
/// partner module asks each one before it removes a role, deactivates the partner or changes its party type, and
/// refuses with the combined list of what is in the way. With none registered nothing blocks, which is right until
/// something can point at a partner.
/// </summary>
public interface IPartnerUsageCheck
{
    /// <param name="roleCode">For <see cref="PartnerIntent.RemoveRole"/>: the role being removed. Otherwise null.</param>
    Task<IReadOnlyList<PartnerUsage>> FindBlockingAsync(int partnerId, PartnerIntent intent, string? roleCode, CancellationToken cancellationToken = default);
}
