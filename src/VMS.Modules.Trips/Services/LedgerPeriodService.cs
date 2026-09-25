using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.5 LR-7 (Recommended Design), §47.2: "POST /api/ledger-periods/{yyyymm}/close · /reopen" —
/// `Ledger.PeriodLock` (Finance Lead, Admin). Closing/reopening a month never touches any ledger entry itself;
/// it only changes whether <see cref="CustomerLedgerPostingService"/>'s own shared <c>SaveEntriesAsync</c> choke
/// point accepts a new posting dated in it.</summary>
public interface ILedgerPeriodService
{
    Task<LedgerPeriodModel> CloseAsync(string yearMonth, LedgerPeriodRequest request, int userId, CancellationToken ct = default);
    Task<LedgerPeriodModel> ReopenAsync(string yearMonth, LedgerPeriodRequest request, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<LedgerPeriodModel>> ListAsync(CancellationToken ct = default);
}

internal sealed class LedgerPeriodService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages) : ILedgerPeriodService
{
    public async Task<LedgerPeriodModel> CloseAsync(string yearMonth, LedgerPeriodRequest request, int userId, CancellationToken ct = default)
    {
        ValidateYearMonth(yearMonth, messages);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));

        var period = await db.LedgerPeriods.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.YearMonth == yearMonth, ct);
        if (period is not null && period.Status == LedgerPeriodStatuses.Closed)
            throw new BusinessRuleException("PERIOD_ALREADY_CLOSED", $"{yearMonth} is already closed.", []);

        if (period is null)
        {
            period = new LedgerPeriod { YearMonth = yearMonth, Status = LedgerPeriodStatuses.Closed, ClosedBy = userId, ClosedOn = DateTime.UtcNow, CloseReason = request.Reason.Trim() };
            db.LedgerPeriods.Add(period);
        }
        else
        {
            period.Status = LedgerPeriodStatuses.Closed;
            period.ClosedBy = userId;
            period.ClosedOn = DateTime.UtcNow;
            period.CloseReason = request.Reason.Trim();
        }
        await db.SaveChangesAsync(ct);
        return ToModel(period);
    }

    public async Task<LedgerPeriodModel> ReopenAsync(string yearMonth, LedgerPeriodRequest request, int userId, CancellationToken ct = default)
    {
        ValidateYearMonth(yearMonth, messages);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));

        var period = await db.LedgerPeriods.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.YearMonth == yearMonth, ct)
            ?? throw new BusinessRuleException("PERIOD_NOT_CLOSED", $"{yearMonth} was never closed.", []);
        if (period.Status != LedgerPeriodStatuses.Closed)
            throw new BusinessRuleException("PERIOD_NOT_CLOSED", $"{yearMonth} is not currently closed.", []);

        period.Status = LedgerPeriodStatuses.Open;
        period.ReopenedBy = userId;
        period.ReopenedOn = DateTime.UtcNow;
        period.ReopenReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        return ToModel(period);
    }

    public async Task<IReadOnlyList<LedgerPeriodModel>> ListAsync(CancellationToken ct = default)
    {
        var periods = await db.LedgerPeriods.AsNoTracking().Where(p => p.TenantId == tenant.TenantId).OrderByDescending(p => p.YearMonth).ToListAsync(ct);
        return periods.Select(ToModel).ToList();
    }

    private static void ValidateYearMonth(string yearMonth, IMessageCatalogue messages)
    {
        if (yearMonth.Length != 6 || !int.TryParse(yearMonth, out _) || !DateOnly.TryParseExact(yearMonth + "01", "yyyyMMdd", out _))
            throw new ValidationException(messages.Error("yearMonth", Msg.Invalid, ("Field", "Period (must be yyyyMM)")));
    }

    private static LedgerPeriodModel ToModel(LedgerPeriod p) => new()
    {
        YearMonth = p.YearMonth, Status = p.Status, ClosedOn = p.ClosedOn, CloseReason = p.CloseReason, ReopenedOn = p.ReopenedOn, ReopenReason = p.ReopenReason
    };
}
