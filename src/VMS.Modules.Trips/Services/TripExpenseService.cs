using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Partners;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip expenses with approval (§29, AC-24).</summary>
public interface ITripExpenseService
{
    Task<IReadOnlyList<TripExpenseModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripExpenseModel> CreateAsync(long tripId, CreateTripExpenseRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripExpenseModel> DecideAsync(long tripExpenseId, DecideTripExpenseRequest request, int decidedByUserId, CancellationToken ct = default);
    Task<TripExpenseModel> VoidAsync(long tripExpenseId, VoidTripExpenseRequest request, int voidedByUserId, CancellationToken ct = default);
    /// <summary>Not one of §47.2's own literal endpoints — added for the driver-sync facade (CC-45) to reload a
    /// single entry by id (e.g. after a replayed <see cref="CreateTripExpenseRequest.ClientEventId"/>).</summary>
    Task<TripExpenseModel> GetAsync(long tripExpenseId, CancellationToken ct = default);
}

internal sealed class TripExpenseService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IMessageCatalogue messages, ILookupReader lookups, IPartnerDirectory partners)
    : ITripExpenseService
{
    public async Task<IReadOnlyList<TripExpenseModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's expenses");
        var rows = await db.TripExpenses.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.TripId == tripId).OrderByDescending(e => e.ExpenseDate).ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    public async Task<TripExpenseModel> CreateAsync(long tripId, CreateTripExpenseRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.Require(trip, caller, scope, PermissionCodes.TRP_EXPENSE_EDIT, "Expense.Edit", "Logging a trip expense");

        // AC-54: a retried offline sync with the same ClientEventId is answered with the original entry.
        if (request.ClientEventId is { } clientId)
        {
            var existing = await db.TripExpenses.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId && e.TripId == tripId && e.ClientEventId == clientId, ct);
            if (existing is not null) return ToModel(existing);
        }

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var type = await lookups.FindAsync(PlatformLookups.TripExpenseType, request.ExpenseTypeId);
        if (type is null || !type.IsActive) Add("expenseTypeId", Msg.Invalid, ("Field", "Expense type"));
        else if (type.Code == "FUEL") Add("expenseTypeId", Msg.Invalid, ("Field", "Expense type (use the Fuel screen to log fuel)"));
        else if (type.Code == "OTHER" && string.IsNullOrWhiteSpace(request.OtherExpenseType))
            Add("otherExpenseType", Msg.Required, ("Field", "Other expense type"));

        if (!TripExpensePaymentMethods.All.Contains(request.PaymentMethod))
            Add("paymentMethod", Msg.OneOf, ("Field", "Payment method"), ("Allowed", string.Join(", ", TripExpensePaymentMethods.All)));

        var amount = request.Amount ?? (request.Quantity is { } qty && request.Rate is { } rate ? qty * rate : (decimal?)null);
        if (amount is null or <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));

        if (request.BusinessPartnerId is { } partnerId)
        {
            var partner = await partners.FindAsync(partnerId, ct);
            if (partner is null) Add("businessPartnerId", Msg.Invalid, ("Field", "Business partner"));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var entry = new TripExpense
        {
            TripId = tripId, ExpenseDate = request.ExpenseDate ?? DateTime.UtcNow, ExpenseTypeId = request.ExpenseTypeId,
            OtherExpenseType = type!.Code == "OTHER" ? Trim(request.OtherExpenseType) : null, Description = Trim(request.Description),
            Quantity = request.Quantity, Rate = request.Rate, Amount = amount!.Value, Reference = Trim(request.Reference),
            BusinessPartnerId = request.BusinessPartnerId, PaymentMethod = request.PaymentMethod, AttachmentId = request.AttachmentId,
            Source = TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual, ClientEventId = request.ClientEventId
        };
        // §29: "Driver-app expenses start Pending ... approved by Ops/Fleet" — a back-office entry is made by
        // someone who already holds the approving role, so it starts Approved rather than needing self-approval.
        entry.ApprovalStatus = entry.Source == TripEventSources.DriverApp ? TripExpenseApprovalStatuses.Pending : TripExpenseApprovalStatuses.Approved;
        if (entry.ApprovalStatus == TripExpenseApprovalStatuses.Approved) { entry.DecidedBy = caller.UserId; entry.DecidedAtUtc = DateTime.UtcNow; }

        db.TripExpenses.Add(entry);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } && request.ClientEventId is not null)
        {
            db.Entry(entry).State = EntityState.Detached;
            var winner = await db.TripExpenses.AsNoTracking()
                .FirstAsync(e => e.TenantId == tenant.TenantId && e.TripId == tripId && e.ClientEventId == request.ClientEventId, ct);
            return ToModel(winner);
        }
        return ToModel(entry);
    }

    public async Task<TripExpenseModel> DecideAsync(long tripExpenseId, DecideTripExpenseRequest request, int decidedByUserId, CancellationToken ct = default)
    {
        var entry = await Find(tripExpenseId, ct);
        if (entry.ApprovalStatus != TripExpenseApprovalStatuses.Pending) throw new ConflictException($"This expense is already {entry.ApprovalStatus}.");
        if (!request.Approved && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Rejection reason")));

        entry.ApprovalStatus = request.Approved ? TripExpenseApprovalStatuses.Approved : TripExpenseApprovalStatuses.Rejected;
        entry.RejectionReason = request.Approved ? null : request.Reason!.Trim();
        entry.DecidedBy = decidedByUserId;
        entry.DecidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToModel(entry);
    }

    public async Task<TripExpenseModel> VoidAsync(long tripExpenseId, VoidTripExpenseRequest request, int voidedByUserId, CancellationToken ct = default)
    {
        var entry = await Find(tripExpenseId, ct);
        if (entry.IsVoided) throw new ConflictException("This expense is already voided.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Void reason")));

        entry.IsVoided = true;
        entry.VoidReason = request.Reason.Trim();
        entry.VoidedBy = voidedByUserId;
        entry.VoidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToModel(entry);
    }

    public async Task<TripExpenseModel> GetAsync(long tripExpenseId, CancellationToken ct = default) => ToModel(await Find(tripExpenseId, ct));

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private async Task<TripExpense> Find(long tripExpenseId, CancellationToken ct) =>
        await db.TripExpenses.FirstOrDefaultAsync(e => e.TenantId == tenant.TenantId && e.TripExpenseId == tripExpenseId, ct)
        ?? throw new NotFoundException($"Trip expense {tripExpenseId} was not found.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TripExpenseModel ToModel(TripExpense e) => new()
    {
        TripExpenseId = e.TripExpenseId, TripId = e.TripId, ExpenseDate = e.ExpenseDate, ExpenseTypeId = e.ExpenseTypeId, OtherExpenseType = e.OtherExpenseType,
        Description = e.Description, Quantity = e.Quantity, Rate = e.Rate, Amount = e.Amount, Reference = e.Reference, BusinessPartnerId = e.BusinessPartnerId,
        PaymentMethod = e.PaymentMethod, AttachmentId = e.AttachmentId, ApprovalStatus = e.ApprovalStatus, RejectionReason = e.RejectionReason, DecidedBy = e.DecidedBy,
        DecidedAtUtc = e.DecidedAtUtc, Source = e.Source, ClientEventId = e.ClientEventId, IsVoided = e.IsVoided, VoidReason = e.VoidReason, VoidedBy = e.VoidedBy, VoidedAtUtc = e.VoidedAtUtc
    };
}
