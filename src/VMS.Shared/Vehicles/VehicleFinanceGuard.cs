namespace VMS.Shared.Vehicles;

/// <summary>
/// Asked before a vehicle's ownership category changes (BR-VH-007): a category other than Bank Leased is refused while a
/// finance agreement still has a balance. The finance module supplies the real answer; until it exists nothing is outstanding.
/// </summary>
public interface IVehicleFinanceGuard
{
    Task<bool> HasOutstandingFinanceAsync(int vehicleId, CancellationToken cancellationToken = default);
}
