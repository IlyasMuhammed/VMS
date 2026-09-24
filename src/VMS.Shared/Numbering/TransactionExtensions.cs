using Microsoft.EntityFrameworkCore;

namespace VMS.Shared.Numbering;

public static class TransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction and commits when it returns. If the caller
    /// is already inside one, joins it. The connection retries transient failures, so the whole of
    /// <paramref name="work"/> can run again: create the entities inside it, not before it.
    /// <code>
    /// await db.InTransactionAsync(async ct =>
    /// {
    ///     var partner = new BusinessPartner { Code = await series.NextAsync(db, NumberSeriesCodes.BusinessPartner) };
    ///     db.Add(partner);
    ///     await db.SaveChangesAsync(ct);
    ///     return partner.Id;
    /// });
    /// </code>
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null) return work(cancellationToken);

        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public static Task InTransactionAsync(this DbContext db, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        db.InTransactionAsync<bool>(async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);
}
