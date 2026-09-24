using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Vehicles.Services;

public interface ITransactionService
{
    /// <summary>The vehicle's ledger, newest first, adjustments included.</summary>
    Task<List<TransactionModel>> ListAsync(int vehicleId);
    Task<TransactionModel> ReverseAsync(int vehicleId, int transactionId, ReverseTransactionRequest request, VehicleCaller caller);
    /// <summary>Attaches a receipt to an installment payment (source §7). Once attached it stays: it is evidence, not a value to correct.</summary>
    Task<TransactionModel> AttachReceiptAsync(int vehicleId, int installmentId, int transactionId, Stream content, string fileName, VehicleCaller caller);
    Task<ReceiptFile> OpenReceiptAsync(int vehicleId, int transactionId);
}

/// <summary>
/// The ledger of a vehicle, and the one way to correct it (BR-VH-011, BR-VH-032). An entry is never edited and never deleted: a reversing
/// entry is posted against it, with a reason, and both stay. Reversing an installment payment also reopens that installment and, if the
/// agreement had been settled by it, the agreement.
/// </summary>
internal sealed class TransactionService(VehicleContext ctx, IFileStore files) : ITransactionService
{
    public async Task<List<TransactionModel>> ListAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var rows = await ctx.Db.Transactions.AsNoTracking().Where(t => t.TenantId == ctx.Tenant && t.VehicleId == vehicleId)
            .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.VehicleTransactionId).ToListAsync();
        var reversed = rows.Where(t => t.ReversesTransactionId is not null).Select(t => t.ReversesTransactionId!.Value).ToHashSet();
        var refs = await ctx.RefsAsync(rows.Select(t => t.PartnerId));
        return rows.Select(t => ToModel(t, refs, reversed.Contains(t.VehicleTransactionId))).ToList();
    }

    private static TransactionModel ToModel(VehicleTransaction t, IReadOnlyDictionary<int, PartnerRef> refs, bool reversed) => new()
    {
        Id = t.VehicleTransactionId, Type = t.Type, SubType = t.SubType, Amount = t.Amount, Date = t.TransactionDate, Partner = VehicleContext.Ref(refs, t.PartnerId), Reference = t.Reference,
        Source = t.Source, IsSystemGenerated = t.IsSystemGenerated, Reason = t.Reason, ReversesTransactionId = t.ReversesTransactionId, IsReversed = reversed,
        HasReceipt = t.ReceiptStorageKey is not null, ReceiptFileName = t.ReceiptFileName, CreatedOn = t.CreatedOn
    };

    public async Task<TransactionModel> ReverseAsync(int vehicleId, int transactionId, ReverseTransactionRequest request, VehicleCaller caller)
    {
        // The person sees what they are reversing.
        if (!caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW)) throw new ForbiddenException("You do not have permission to reverse vehicle entries.");

        await ctx.LoadReadOnlyAsync(vehicleId);
        var original = await ctx.Db.Transactions.FirstOrDefaultAsync(t => t.TenantId == ctx.Tenant && t.VehicleId == vehicleId && t.VehicleTransactionId == transactionId)
            ?? throw new NotFoundException("Entry not found.");

        var today = await ctx.TodayAsync();
        var date = request.Date ?? today;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        if (original.Type == TransactionTypes.Adjustment) Add("transactionId", Msg.VhReversalNotReversible);
        else if (await ctx.Db.Transactions.AnyAsync(t => t.TenantId == ctx.Tenant && t.ReversesTransactionId == transactionId)) Add("transactionId", Msg.VhAlreadyReversed);
        if (reason is null) Add("reason", Msg.Required, ("Field", "Reason"));
        else if (reason.Length > 500) Add("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500));
        if (date > today) Add("date", Msg.NotFuture, ("Field", "Date"));
        else if (date < original.TransactionDate) Add("date", Msg.Min, ("Field", "Date"), ("Min", original.TransactionDate.ToString("yyyy-MM-dd")));
        if (errors.Count > 0) throw new ValidationException(errors);

        var reference = $"Reversal of {original.Reference}";
        var reversal = new VehicleTransaction
        {
            VehicleId = vehicleId, Type = TransactionTypes.Adjustment, SubType = original.Type, Amount = -original.Amount, TransactionDate = date, PartnerId = original.PartnerId,
            Reference = reference.Length <= 120 ? reference : reference[..120], Source = TransactionSources.Manual, IsSystemGenerated = false, ReversesTransactionId = original.VehicleTransactionId,
            Reason = reason, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
        };

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                // A payment that is reversed is no longer paid: its installment goes back to what it was, and an agreement it settled is open again.
                if (original.Type == TransactionTypes.Installment && original.InstallmentId is { } installmentId)
                {
                    var installment = await ctx.Db.Installments.FirstAsync(i => i.TenantId == ctx.Tenant && i.VehicleInstallmentId == installmentId, ct);
                    installment.PaidAmount = Math.Max(0m, installment.PaidAmount - original.Amount);
                    installment.Status = installment.PaidAmount <= 0m ? InstallmentStatuses.Pending : installment.PaidAmount >= installment.ExpectedAmount ? InstallmentStatuses.Paid : InstallmentStatuses.PartiallyPaid;
                    if (installment.PaidAmount <= 0m) installment.PaidOn = null;
                    var agreement = await ctx.Db.Agreements.FirstAsync(a => a.TenantId == ctx.Tenant && a.VehicleFinanceAgreementId == installment.VehicleFinanceAgreementId, ct);
                    if (agreement.Status == AgreementStatuses.Settled)
                    {
                        agreement.Status = AgreementStatuses.Active;
                        agreement.ModifiedBy = caller.UserId;
                        agreement.ModifiedOn = DateTime.UtcNow;
                    }
                }
                ctx.Db.Transactions.Add(reversal);
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ValidationException(ctx.Messages.Error("transactionId", Msg.VhAlreadyReversed));   // someone reversed it at the same moment
        }

        var refs = await ctx.RefsAsync([reversal.PartnerId]);
        return ToModel(reversal, refs, reversed: false);
    }

    // ── Receipts ────────────────────────────────────────────────────────────────────

    public async Task<TransactionModel> AttachReceiptAsync(int vehicleId, int installmentId, int transactionId, Stream content, string fileName, VehicleCaller caller)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var entry = await ctx.Db.Transactions.FirstOrDefaultAsync(t => t.TenantId == ctx.Tenant && t.VehicleId == vehicleId && t.VehicleTransactionId == transactionId
            && t.Type == TransactionTypes.Installment && t.InstallmentId == installmentId) ?? throw new NotFoundException("Entry not found.");
        if (entry.ReceiptStorageKey is not null) throw new ValidationException(ctx.Messages.Error("file", Msg.VhReceiptAlreadyAttached));

        var stored = await files.SaveAsync(new FileUpload(content, fileName, new FileOwner("VehicleTransaction", transactionId.ToString()), FileRules.Scans, ctx.Tenant));
        entry.ReceiptStorageKey = stored.StorageKey;
        entry.ReceiptSha256 = stored.Sha256;
        entry.ReceiptContentType = stored.ContentType;
        entry.ReceiptFileName = stored.OriginalFileName;
        entry.ReceiptSizeBytes = stored.SizeBytes;
        await ctx.Db.SaveChangesAsync();

        var refs = await ctx.RefsAsync([entry.PartnerId]);
        var reversed = await ctx.Db.Transactions.AnyAsync(t => t.TenantId == ctx.Tenant && t.ReversesTransactionId == transactionId);
        return ToModel(entry, refs, reversed);
    }

    public async Task<ReceiptFile> OpenReceiptAsync(int vehicleId, int transactionId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var entry = await ctx.Db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == ctx.Tenant && t.VehicleId == vehicleId && t.VehicleTransactionId == transactionId)
            ?? throw new NotFoundException("Entry not found.");
        if (entry.ReceiptStorageKey is null) throw new NotFoundException("No receipt is attached to this entry.");

        Stream read;
        try { read = await files.OpenReadAsync(ctx.Tenant, entry.ReceiptStorageKey); }
        catch (FileIntegrityException) { throw new ConflictException("The stored receipt failed its integrity check and was not served."); }
        await using var _ = read;
        var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);
        buffer.Position = 0;

        // The decryption tag already proves the bytes were not altered; this checks them against what was recorded at upload, the same
        // double layer the download-link route uses, so a receipt that no longer matches what was uploaded is refused rather than served.
        var actual = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        if (!string.Equals(actual, entry.ReceiptSha256, StringComparison.OrdinalIgnoreCase)) throw new ConflictException("The stored receipt no longer matches its recorded checksum and was not served.");
        buffer.Position = 0;

        return new ReceiptFile(buffer, entry.ReceiptContentType ?? "application/octet-stream", entry.ReceiptFileName ?? "receipt");
    }
}
