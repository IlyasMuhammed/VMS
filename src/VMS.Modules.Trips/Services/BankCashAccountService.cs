using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>§37: the company's own bank accounts that receive customer payments. Built now (CC-31); real seed
/// data waits on Q3 — until then, Admin/Finance enter accounts by hand through this CRUD.</summary>
public interface IBankCashAccountService
{
    Task<IReadOnlyList<BankCashAccountModel>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<BankCashAccountModel> CreateAsync(SaveBankCashAccountRequest request, CancellationToken ct = default);
    Task<BankCashAccountModel> UpdateAsync(long bankCashAccountId, SaveBankCashAccountRequest request, CancellationToken ct = default);
}

internal sealed class BankCashAccountService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages) : IBankCashAccountService
{
    public async Task<IReadOnlyList<BankCashAccountModel>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var accounts = await db.BankCashAccounts.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && (includeInactive || a.IsActive))
            .OrderBy(a => a.AccountTitle).ToListAsync(ct);
        return accounts.Select(ToModel).ToList();
    }

    public async Task<BankCashAccountModel> CreateAsync(SaveBankCashAccountRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var account = new BankCashAccount
        {
            AccountTitle = request.AccountTitle.Trim(), BankName = request.BankName.Trim(), BranchName = request.BranchName.Trim(),
            AccountNumberLast4 = request.AccountNumberLast4.Trim(), CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "PKR" : request.CurrencyCode.Trim().ToUpperInvariant(),
            IsActive = request.IsActive
        };
        db.BankCashAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return ToModel(account);
    }

    public async Task<BankCashAccountModel> UpdateAsync(long bankCashAccountId, SaveBankCashAccountRequest request, CancellationToken ct = default)
    {
        Validate(request);
        if (string.IsNullOrWhiteSpace(request.RowVersion))
            throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        var account = await db.BankCashAccounts.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.BankCashAccountId == bankCashAccountId, ct)
            ?? throw new NotFoundException($"Bank account {bankCashAccountId} was not found.");

        account.AccountTitle = request.AccountTitle.Trim(); account.BankName = request.BankName.Trim(); account.BranchName = request.BranchName.Trim();
        account.AccountNumberLast4 = request.AccountNumberLast4.Trim(); account.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.CurrencyCode)) account.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        db.Entry(account).Property(a => a.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException("This bank account was changed by someone else. Reload and try again."); }

        return ToModel(account);
    }

    private void Validate(SaveBankCashAccountRequest request)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.AccountTitle)) errors.Add(messages.Error("accountTitle", Msg.Required, ("Field", "Account title")));
        if (string.IsNullOrWhiteSpace(request.BankName)) errors.Add(messages.Error("bankName", Msg.Required, ("Field", "Bank name")));
        if (string.IsNullOrWhiteSpace(request.BranchName)) errors.Add(messages.Error("branchName", Msg.Required, ("Field", "Branch name")));
        if (string.IsNullOrWhiteSpace(request.AccountNumberLast4) || request.AccountNumberLast4.Trim().Length != 4 || !request.AccountNumberLast4.Trim().All(char.IsDigit))
            errors.Add(messages.Error("accountNumberLast4", Msg.Invalid, ("Field", "Account number (last 4 digits)")));
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static BankCashAccountModel ToModel(BankCashAccount a) => new()
    {
        BankCashAccountId = a.BankCashAccountId, AccountTitle = a.AccountTitle, BankName = a.BankName, BranchName = a.BranchName,
        AccountNumberLast4 = a.AccountNumberLast4, CurrencyCode = a.CurrencyCode, IsActive = a.IsActive, RowVersion = Convert.ToBase64String(a.RowVersion)
    };
}
