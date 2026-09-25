using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Data;

/// <summary>
/// The Trip, Billing, Invoicing &amp; Customer Ledger module's own schema (second FSD, CC-01). One schema, one
/// context, deliberately (see <c>Document/VMS-TripBilling-Implementation-Notes.md</c> §3): the ledger's own rule
/// that every entry posts in the same DB transaction as its cause is what a single <see cref="DbContext"/> gives
/// for free, and every entity here (Customer → Trip → Invoice → Ledger) is part of one lifecycle, the same way
/// the Vehicles module bundles Vehicle + Finance + RecurringCharges as one project rather than several.
/// </summary>
internal sealed class TripsDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "trp";

    private readonly IAuditContext _audit;

    public ITenantContext TenantContext { get; }

    public TripsDbContext(DbContextOptions<TripsDbContext> options, ITenantContext tenantContext, IAuditContext audit) : base(options)
    {
        TenantContext = tenantContext;
        _audit = audit;
    }

    internal DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    internal DbSet<Currency> Currencies => Set<Currency>();
    internal DbSet<CurrencySettings> CurrencySettings => Set<CurrencySettings>();
    internal DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    internal DbSet<Customer> Customers => Set<Customer>();
    internal DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    internal DbSet<CustomerBillingAddress> CustomerBillingAddresses => Set<CustomerBillingAddress>();
    internal DbSet<CustomerBillingConfiguration> CustomerBillingConfigurations => Set<CustomerBillingConfiguration>();
    internal DbSet<CustomerTaxRule> CustomerTaxRules => Set<CustomerTaxRule>();
    internal DbSet<CustomerInvoiceTemplate> CustomerInvoiceTemplates => Set<CustomerInvoiceTemplate>();
    internal DbSet<City> Cities => Set<City>();
    internal DbSet<Route> Routes => Set<Route>();
    internal DbSet<RouteStop> RouteStops => Set<RouteStop>();
    internal DbSet<TripConfiguration> TripConfigurations => Set<TripConfiguration>();
    internal DbSet<TripConfigurationStop> TripConfigurationStops => Set<TripConfigurationStop>();
    internal DbSet<TripConfigurationVehicle> TripConfigurationVehicles => Set<TripConfigurationVehicle>();
    internal DbSet<TripRate> TripRates => Set<TripRate>();
    internal DbSet<TripNumberCounter> TripNumberCounters => Set<TripNumberCounter>();
    internal DbSet<Trip> Trips => Set<Trip>();
    internal DbSet<TripStop> TripStops => Set<TripStop>();
    internal DbSet<TripEvent> TripEvents => Set<TripEvent>();
    internal DbSet<TripDocument> TripDocuments => Set<TripDocument>();
    internal DbSet<TripPOD> TripPODs => Set<TripPOD>();
    internal DbSet<TripIssue> TripIssues => Set<TripIssue>();
    internal DbSet<TripRateHistory> TripRateHistories => Set<TripRateHistory>();
    internal DbSet<FuelCard> FuelCards => Set<FuelCard>();
    internal DbSet<FuelCardAssignment> FuelCardAssignments => Set<FuelCardAssignment>();
    internal DbSet<TripFuel> TripFuels => Set<TripFuel>();
    internal DbSet<TripExpense> TripExpenses => Set<TripExpense>();
    internal DbSet<TripIncome> TripIncomes => Set<TripIncome>();
    internal DbSet<Invoice> Invoices => Set<Invoice>();
    internal DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    internal DbSet<InvoiceTripLink> InvoiceTripLinks => Set<InvoiceTripLink>();
    internal DbSet<InvoiceAdjustment> InvoiceAdjustments => Set<InvoiceAdjustment>();
    internal DbSet<InvoiceTaxLine> InvoiceTaxLines => Set<InvoiceTaxLine>();
    internal DbSet<InvoiceHistory> InvoiceHistories => Set<InvoiceHistory>();
    internal DbSet<InvoiceReplacement> InvoiceReplacements => Set<InvoiceReplacement>();
    internal DbSet<InvoiceNumberCounter> InvoiceNumberCounters => Set<InvoiceNumberCounter>();
    internal DbSet<InvoiceDocument> InvoiceDocuments => Set<InvoiceDocument>();
    internal DbSet<InvoiceEvidence> InvoiceEvidences => Set<InvoiceEvidence>();
    internal DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
    internal DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();
    internal DbSet<LedgerNumberCounter> LedgerNumberCounters => Set<LedgerNumberCounter>();
    internal DbSet<CustomerLedgerSequenceCounter> CustomerLedgerSequenceCounters => Set<CustomerLedgerSequenceCounter>();
    internal DbSet<BankCashAccount> BankCashAccounts => Set<BankCashAccount>();
    internal DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    internal DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    internal DbSet<ReceiptNumberCounter> ReceiptNumberCounters => Set<ReceiptNumberCounter>();
    internal DbSet<InvoiceSettlement> InvoiceSettlements => Set<InvoiceSettlement>();
    internal DbSet<SettlementNumberCounter> SettlementNumberCounters => Set<SettlementNumberCounter>();
    internal DbSet<CustomerAdvance> CustomerAdvances => Set<CustomerAdvance>();
    internal DbSet<AdvanceNumberCounter> AdvanceNumberCounters => Set<AdvanceNumberCounter>();
    internal DbSet<InvoicePaymentTransfer> InvoicePaymentTransfers => Set<InvoicePaymentTransfer>();
    internal DbSet<TransferNumberCounter> TransferNumberCounters => Set<TransferNumberCounter>();
    internal DbSet<InvoiceCreditCarryForward> InvoiceCreditCarryForwards => Set<InvoiceCreditCarryForward>();
    internal DbSet<CustomerRefund> CustomerRefunds => Set<CustomerRefund>();
    internal DbSet<LedgerReconciliationRun> LedgerReconciliationRuns => Set<LedgerReconciliationRun>();
    internal DbSet<LedgerReconciliationMismatch> LedgerReconciliationMismatches => Set<LedgerReconciliationMismatch>();
    internal DbSet<LedgerPeriod> LedgerPeriods => Set<LedgerPeriod>();
    internal DbSet<ReportExportJob> ReportExportJobs => Set<ReportExportJob>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripsDbContext).Assembly);
        modelBuilder.MapAuditEntries();   // the Core module owns the table; this context only writes to it
        modelBuilder.ApplyTenantQueryFilters(this);
        modelBuilder.ApplyTenantIndexes();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.StampTenantScopedEntities(TenantContext);
        return this.SaveAudited(_audit, TenantContext.TenantId, acceptAllChangesOnSuccess, a => base.SaveChanges(a));
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        this.StampTenantScopedEntities(TenantContext);
        return this.SaveAuditedAsync(_audit, TenantContext.TenantId, acceptAllChangesOnSuccess,
            (a, ct) => base.SaveChangesAsync(a, ct), cancellationToken);
    }
}
