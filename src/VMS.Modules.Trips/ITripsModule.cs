using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Services;

namespace VMS.Modules.Trips;

public interface ITripsModule { }

public static class TripsModuleExtensions
{
    public static IServiceCollection AddTripsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<TripsDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", TripsDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ICurrencySeeder, CurrencySeeder>();
        services.AddScoped<ICurrencyService, CurrencyService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerLookup, CustomerLookup>();
        services.AddScoped<ICustomerContactService, CustomerContactService>();
        services.AddScoped<ICustomerBillingAddressService, CustomerBillingAddressService>();
        services.AddScoped<ICustomerBillingConfigurationService, CustomerBillingConfigurationService>();
        services.AddScoped<ICustomerTaxRuleService, CustomerTaxRuleService>();
        services.AddScoped<ICustomerInvoiceTemplateService, CustomerInvoiceTemplateService>();
        services.AddScoped<ICustomerActivationRequirement, RequiresActiveInvoiceTemplateRequirement>();
        services.AddScoped<ICitySeeder, CitySeeder>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<IRouteService, RouteService>();
        services.AddScoped<ITripConfigurationService, TripConfigurationService>();
        services.AddScoped<ITripRateService, TripRateService>();
        services.AddScoped<ITripNumberAllocator, TripNumberAllocator>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<ITripSearchService, TripSearchService>();
        services.AddScoped<ITripEventRecorder, TripEventRecorder>();
        services.AddScoped<ITripLifecycleService, TripLifecycleService>();
        services.AddScoped<ITripEventService, TripEventService>();
        services.AddScoped<ITripDocumentService, TripDocumentService>();
        services.AddScoped<ITripPODService, TripPODService>();
        services.AddScoped<ITripIssueService, TripIssueService>();
        services.AddScoped<ITripRepricingService, TripRepricingService>();
        services.AddScoped<ICustomerActivationRequirement, RequiresActiveContactRequirement>();
        services.AddScoped<ICustomerActivationRequirement, RequiresDefaultBillingAddressRequirement>();
        services.AddScoped<ICustomerActivationRequirement, RequiresBillingConfigurationRequirement>();
        services.AddScoped<IFuelCardService, FuelCardService>();
        services.AddScoped<IFuelCardExpiryJob, FuelCardExpiryJob>();
        services.AddHostedService<FuelCardHostedService>();
        services.AddScoped<ITripFuelService, TripFuelService>();
        services.AddScoped<ITripExpenseService, TripExpenseService>();
        services.AddScoped<ITripIncomeService, TripIncomeService>();
        services.AddScoped<ITripPnLService, TripPnLService>();
        services.AddScoped<IInvoiceEligibilityService, InvoiceEligibilityService>();
        services.AddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
        services.AddScoped<IInvoiceCreationService, InvoiceCreationService>();
        services.AddScoped<ILedgerNumberAllocator, LedgerNumberAllocator>();
        services.AddScoped<ICustomerLedgerSequenceAllocator, CustomerLedgerSequenceAllocator>();
        services.AddScoped<ICustomerLedgerPostingService, CustomerLedgerPostingService>();
        services.AddScoped<IInvoiceSubmissionService, InvoiceSubmissionService>();
        services.AddScoped<IBankCashAccountService, BankCashAccountService>();
        services.AddScoped<IReceiptNumberAllocator, ReceiptNumberAllocator>();
        services.AddScoped<IPaymentReceiptService, PaymentReceiptService>();
        services.AddScoped<IPaymentReversalService, PaymentReversalService>();
        services.AddScoped<ISettlementNumberAllocator, SettlementNumberAllocator>();
        services.AddScoped<IInvoiceSettlementService, InvoiceSettlementService>();
        services.AddScoped<IAdvanceNumberAllocator, AdvanceNumberAllocator>();
        services.AddScoped<IAdvanceService, AdvanceService>();
        services.AddScoped<ITransferNumberAllocator, TransferNumberAllocator>();
        services.AddScoped<IInvoicePaymentTransferService, InvoicePaymentTransferService>();
        services.AddScoped<IInvoiceRegenerationService, InvoiceRegenerationService>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddScoped<ICustomerStatementPdfRenderer, CustomerStatementPdfRenderer>();
        services.AddScoped<ICustomerLedgerReadService, CustomerLedgerReadService>();
        services.AddScoped<ILedgerPeriodService, LedgerPeriodService>();
        services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
        services.AddScoped<ILedgerReportService, LedgerReportService>();
        services.AddHostedService<ReportExportHostedService>();
        services.AddScoped<ILedgerReconciliationJob, LedgerReconciliationJob>();
        services.AddHostedService<LedgerReconciliationHostedService>();
        services.AddScoped<IInvoicePdfRenderer, InvoicePdfRenderer>();
        services.AddScoped<IInvoiceDocumentService, InvoiceDocumentService>();
        services.AddScoped<IInvoiceEvidenceRenderer, InvoiceEvidenceRenderer>();
        services.AddScoped<IInvoiceEvidenceGenerationService, InvoiceEvidenceGenerationService>();
        services.AddHostedService<InvoiceEvidenceHostedService>();
        // §34's own PDF rendering needs QuestPDF's free Community licence accepted once at startup.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        return services;
    }

    /// <summary>Applies pending migrations. Runs after BusinessPartners, Documents and Vehicles (this module's
    /// later tasks read them through <c>IPartnerDirectory</c>/<c>IVehicleDirectory</c>).</summary>
    public static IApplicationBuilder UseTripsModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<TripsDbContext>().Database.Migrate();
        return app;
    }
}
