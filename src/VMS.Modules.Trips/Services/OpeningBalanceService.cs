using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A L16, LR-11: the go-live opening balance — buildable now (schema, posting, importer), loading
/// real customer data stays blocked on Q4 (register's own "Needs input" table). Every posted balance links to a
/// pseudo-invoice <c>OB-{CustomerCode}</c> (§40A.1's own literal field table), a real but deliberately inert
/// <c>Invoice</c> row: <see cref="InvoiceStatuses"/> pointedly does NOT include its status, and it is always
/// <c>IsActive = false</c>, so it is invisible to every existing invoice list/overlap/eligibility/reconciliation
/// query without any of them needing to know it exists.</summary>
public interface IOpeningBalanceService
{
    Task<OpeningBalanceModel> PostAsync(int customerId, PostOpeningBalanceRequest request, int userId, CancellationToken ct = default);
    Task<OpeningBalanceImportResultModel> ImportAsync(ImportOpeningBalancesRequest request, int userId, CancellationToken ct = default);
}

internal sealed class OpeningBalanceService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ICustomerLedgerPostingService ledger) : IOpeningBalanceService
{
    /// <summary>Sentinel <c>Invoice.Status</c> for the pseudo-invoice — deliberately absent from
    /// <see cref="InvoiceStatuses.All"/> so nothing that iterates or validates against that list ever needs to
    /// account for it. Kept to 10 characters or fewer: <c>Invoice.Status</c> is <c>nvarchar(10)</c> (sized for
    /// the five real statuses, the longest being "Submitted"/"Cancelled"/"Generated" at 9), so anything longer
    /// fails outright rather than silently truncating.</summary>
    private const string PseudoInvoiceStatus = "OpeningBal";

    public async Task<OpeningBalanceModel> PostAsync(int customerId, PostOpeningBalanceRequest request, int userId, CancellationToken ct = default)
    {
        if (request.Amount == 0)
            throw new ValidationException(messages.Error("amount", Msg.Invalid, ("Field", "Amount (must be non-zero)")));

        return await db.InTransactionAsync(async ct2 =>
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct2)
                ?? throw new NotFoundException($"Customer {customerId} was not found.");
            var currency = string.IsNullOrWhiteSpace(request.CurrencyCode) ? customer.CurrencyCode : request.CurrencyCode;

            // LR-11: "posted once per customer and currency."
            var alreadyPosted = await db.CustomerLedgerEntries.AnyAsync(e => e.TenantId == tenant.TenantId && e.CustomerId == customerId
                && e.CurrencyCode == currency && e.EntryType == LedgerEntryTypes.OpeningBalance, ct2);
            if (alreadyPosted)
                throw new BusinessRuleException("OPENING_BALANCE_ALREADY_POSTED", $"An opening balance for {customer.CustomerCode} ({currency}) has already been posted.", []);

            var pseudoInvoice = await FindOrCreatePseudoInvoiceAsync(customer, ct2);

            var narration = $"Opening balance as of {request.AsOfDate:dd-MMM-yyyy}" + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $" — {request.Reason.Trim()}");
            await ledger.PostOpeningBalanceAsync(customerId, currency, pseudoInvoice.InvoiceId, pseudoInvoice.InvoiceNumber,
                debit: request.Amount > 0 ? request.Amount : 0, credit: request.Amount < 0 ? -request.Amount : 0, request.AsOfDate, narration, userId, ct2);

            return new OpeningBalanceModel
            {
                CustomerId = customerId, CustomerCode = customer.CustomerCode, CurrencyCode = currency,
                Amount = request.Amount, AsOfDate = request.AsOfDate, PseudoInvoiceNumber = pseudoInvoice.InvoiceNumber
            };
        }, ct);
    }

    public async Task<OpeningBalanceImportResultModel> ImportAsync(ImportOpeningBalancesRequest request, int userId, CancellationToken ct = default)
    {
        var results = new List<OpeningBalanceImportRowResult>();
        foreach (var row in request.Rows)
        {
            try
            {
                var customerId = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.CustomerCode == row.CustomerCode)
                    .Select(c => c.CustomerId).FirstOrDefaultAsync(ct);
                if (customerId == 0)
                {
                    results.Add(new OpeningBalanceImportRowResult { CustomerCode = row.CustomerCode, Success = false, Error = "Customer not found." });
                    continue;
                }

                await PostAsync(customerId, new PostOpeningBalanceRequest { Amount = row.Amount, CurrencyCode = row.CurrencyCode, AsOfDate = row.AsOfDate, Reason = row.Reason }, userId, ct);
                results.Add(new OpeningBalanceImportRowResult { CustomerCode = row.CustomerCode, Success = true });
            }
            catch (Exception ex) when (ex is ValidationException or BusinessRuleException or NotFoundException)
            {
                // A bad row never aborts the batch — a genuine "importer" processes every row it can and reports
                // the rest, rather than an all-or-nothing transaction across unrelated customers.
                results.Add(new OpeningBalanceImportRowResult { CustomerCode = row.CustomerCode, Success = false, Error = ex.Message });
            }
        }

        return new OpeningBalanceImportResultModel
        {
            TotalRows = results.Count, Succeeded = results.Count(r => r.Success), Failed = results.Count(r => !r.Success), Results = results
        };
    }

    private async Task<Invoice> FindOrCreatePseudoInvoiceAsync(Customer customer, CancellationToken ct)
    {
        var invoiceNumber = $"OB-{customer.CustomerCode}";
        var existing = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceNumber == invoiceNumber, ct);
        if (existing is not null) return existing;

        var pseudo = new Invoice
        {
            InvoiceNumber = invoiceNumber, Version = 1, RootInvoiceId = 0, CustomerId = customer.CustomerId,
            PeriodFrom = DateOnly.FromDateTime(DateTime.UtcNow), PeriodTo = DateOnly.FromDateTime(DateTime.UtcNow), InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName, Ntn = customer.Ntn, Strn = customer.Strn, CurrencyCode = customer.CurrencyCode,
            Status = PseudoInvoiceStatus, PaymentStatus = InvoicePaymentStatuses.Unpaid, IsActive = false, GeneratedOn = DateTime.UtcNow,
            Remarks = "System pseudo-invoice for the customer's go-live opening balance — never Submitted, Cancelled or Regenerated."
        };
        db.Invoices.Add(pseudo);
        await db.SaveChangesAsync(ct);
        pseudo.RootInvoiceId = pseudo.InvoiceId;
        await db.SaveChangesAsync(ct);
        return pseudo;
    }
}
