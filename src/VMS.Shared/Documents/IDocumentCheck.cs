namespace VMS.Shared.Documents;

/// <summary>One missing document, for a save-time warning or an activation gate.</summary>
public sealed record MissingDocument(string TypeCode, string TypeName, bool Required);

/// <summary>
/// What another module needs to know about a record's documents, without depending on the Documents module directly (BR-VH-015,
/// BR-BP-005, BR-DOC-004): the same cross-module pattern as <c>IPartnerDirectory</c> and <c>IBranchDirectory</c>. Implemented by
/// the Documents module; a caller with nothing registered gets <see cref="CanCheck"/> false rather than a silent false negative.
/// </summary>
public interface IDocumentCheck
{
    bool CanCheck { get; }

    /// <summary>The mandatory or warn-level document types missing for this owner (types not narrowed to a role the owner does not have), oldest rule first.</summary>
    Task<IReadOnlyList<MissingDocument>> MissingAsync(string ownerType, int ownerId, IReadOnlySet<string> partnerRoles, CancellationToken cancellationToken = default);
}
