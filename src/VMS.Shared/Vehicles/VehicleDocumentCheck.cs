namespace VMS.Shared.Vehicles;

/// <summary>
/// Asked when a vehicle is activated (BR-VH-015, VAL-VH-014): a Self Owned or Bank Leased vehicle needs its registration book on file.
/// The documents module supplies the real answer. Until it exists nothing can be checked, so the rule is treated as met and the
/// vehicle module says so on the activation checklist; the documents module must register its own implementation.
/// </summary>
public interface IVehicleDocumentCheck
{
    /// <summary>False while the document rule cannot be checked at all, so the checklist can say it is not being enforced.</summary>
    bool CanCheck { get; }

    Task<bool> HasRegistrationBookAsync(int vehicleId, CancellationToken cancellationToken = default);
}

/// <summary>The stand-in until the documents module exists: nothing can be checked, so nothing is missing.</summary>
public sealed class NoDocumentCheck : IVehicleDocumentCheck
{
    public bool CanCheck => false;
    public Task<bool> HasRegistrationBookAsync(int vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
