using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Trips.Domain;

namespace VMS.Modules.Trips.Data;

internal sealed class IdempotencyRecordMap : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords");
        b.HasKey(x => x.IdempotencyRecordId);
        b.Property(x => x.Key).HasMaxLength(200).IsRequired();
        b.Property(x => x.Route).HasMaxLength(200).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.ResponseContentType).HasMaxLength(100);

        // A replay is looked up by exactly this triple; a second POST with the same key while the first save
        // is still in flight must fail here rather than run the business logic twice.
        b.HasIndex(x => new { x.TenantId, x.Key, x.Route }).IsUnique();
    }
}

internal sealed class CurrencyMap : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> b)
    {
        b.ToTable("Currencies");
        b.HasKey(x => x.CurrencyId);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.CurrencyName).HasMaxLength(50).IsRequired();
        b.Property(x => x.Symbol).HasMaxLength(5);
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CurrencyCode }).IsUnique();
        // "Exactly one base currency per tenant" (§13A) — kept by the database, not only by the service.
        b.HasIndex(x => x.TenantId).HasFilter("[IsBase] = 1").IsUnique();
    }
}

internal sealed class CurrencySettingsMap : IEntityTypeConfiguration<CurrencySettings>
{
    public void Configure(EntityTypeBuilder<CurrencySettings> b)
    {
        b.ToTable("CurrencySettings");
        b.HasKey(x => x.CurrencySettingsId);
        b.Property(x => x.BaseCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();

        b.HasIndex(x => x.TenantId).IsUnique();   // exactly one settings row per tenant
    }
}

internal sealed class CustomerMap : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.CustomerId);
        b.Property(x => x.CustomerCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(50);
        b.Property(x => x.AddressLine1).HasMaxLength(200).IsRequired();
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.ProvinceState).HasMaxLength(100);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.Ntn).HasMaxLength(20);
        b.Property(x => x.Strn).HasMaxLength(30);
        b.Property(x => x.OtherRegistrationNo).HasMaxLength(50);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.CreditLimit).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.InactiveReason).HasMaxLength(500);
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        // "Unique across the tenant, case-insensitive" (§10) — the code is always stored upper-cased, so the
        // database's default (case-insensitive) collation on a plain unique index already gives this for free.
        b.HasIndex(x => new { x.TenantId, x.CustomerCode }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
    }
}

internal sealed class CustomerContactMap : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> b)
    {
        b.ToTable("CustomerContacts");
        b.HasKey(x => x.CustomerContactId);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Designation).HasMaxLength(100);
        b.Property(x => x.Mobile1).HasMaxLength(20).IsRequired();
        b.Property(x => x.Mobile2).HasMaxLength(20);
        b.Property(x => x.Telephone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(150);
        b.Property(x => x.AvailabilityTime).HasMaxLength(100);
        b.Property(x => x.Purpose).HasMaxLength(50);
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        // "Max one active primary per customer" (§11) — kept by the database, not only by the service.
        b.HasIndex(x => x.CustomerId).HasFilter("[IsPrimary] = 1 AND [Status] = 'Active'").IsUnique();
    }
}

internal sealed class CustomerBillingAddressMap : IEntityTypeConfiguration<CustomerBillingAddress>
{
    public void Configure(EntityTypeBuilder<CustomerBillingAddress> b)
    {
        b.ToTable("CustomerBillingAddresses");
        b.HasKey(x => x.CustomerBillingAddressId);
        b.Property(x => x.AddressName).HasMaxLength(100).IsRequired();
        b.Property(x => x.AddressLine1).HasMaxLength(200).IsRequired();
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.ProvinceState).HasMaxLength(100);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.Ntn).HasMaxLength(20);
        b.Property(x => x.Strn).HasMaxLength(30);
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasIndex(x => new { x.CustomerId, x.AddressName }).IsUnique();   // unique per customer, not tenant-wide
        // "Exactly one active default per customer" (§12) — kept by the database, not only by the service.
        b.HasIndex(x => x.CustomerId).HasFilter("[IsDefault] = 1 AND [Status] = 'Active'").IsUnique();
    }
}

internal sealed class CustomerBillingConfigurationMap : IEntityTypeConfiguration<CustomerBillingConfiguration>
{
    public void Configure(EntityTypeBuilder<CustomerBillingConfiguration> b)
    {
        b.ToTable("CustomerBillingConfigurations");
        b.HasKey(x => x.CustomerBillingConfigurationId);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.InvoiceNumberPrefix).HasMaxLength(10).IsRequired();
        b.Property(x => x.DuplicateReferenceBehaviour).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        // "One configuration record per customer" in force at a time — kept by the database, not only the service.
        b.HasIndex(x => x.CustomerId).HasFilter("[EffectiveTo] IS NULL").IsUnique();
    }
}

internal sealed class CustomerTaxRuleMap : IEntityTypeConfiguration<CustomerTaxRule>
{
    public void Configure(EntityTypeBuilder<CustomerTaxRule> b)
    {
        b.ToTable("CustomerTaxRules");
        b.HasKey(x => x.CustomerTaxRuleId);
        b.Property(x => x.TaxName).HasMaxLength(100).IsRequired();
        b.Property(x => x.TaxNameKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.TaxCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.TaxType).HasMaxLength(12).IsRequired();
        b.Property(x => x.TaxPercentage).HasColumnType("decimal(9,4)");
        b.Property(x => x.FixedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CalculationBasis).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        // Overlap/replacement matching key: "same tax name (case-insensitively) and tax code" (§14).
        b.HasIndex(x => new { x.CustomerId, x.TaxNameKey, x.TaxCode });
        // At most one *open* (current) version per (customer, tax name, tax code) — kept by the database too,
        // not only by the replace-with-confirmation flow.
        b.HasIndex(x => new { x.CustomerId, x.TaxNameKey, x.TaxCode }).HasFilter("[EffectiveTo] IS NULL").IsUnique();
    }
}

internal sealed class CustomerInvoiceTemplateMap : IEntityTypeConfiguration<CustomerInvoiceTemplate>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceTemplate> b)
    {
        b.ToTable("CustomerInvoiceTemplates");
        b.HasKey(x => x.CustomerInvoiceTemplateId);
        b.Property(x => x.TemplateName).HasMaxLength(100).IsRequired();
        b.Property(x => x.TemplateType).HasMaxLength(20).IsRequired();
        b.Property(x => x.TemplateReference).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        // At most one open *Active* version per (customer, template name) — scoped to Active (not any EffectiveTo-
        // null row) so a Draft new-version can be prepared while the current Active one is still in force; the
        // constraint bites only when someone tries to activate a second one for the same name.
        b.HasIndex(x => new { x.CustomerId, x.TemplateName }).HasFilter("[EffectiveTo] IS NULL AND [Status] = 'Active'").IsUnique();
        // "One default per customer" (§15), across every template name — same pattern as CustomerBillingAddress.
        b.HasIndex(x => x.CustomerId).HasFilter("[IsDefault] = 1 AND [Status] = 'Active'").IsUnique();
    }
}

internal sealed class CityMap : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> b)
    {
        b.ToTable("Cities");
        b.HasKey(x => x.CityId);
        b.Property(x => x.CityName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Abbreviation).HasMaxLength(5).IsRequired();
        b.Property(x => x.ProvinceState).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();

        // "Unique (global)" (§16) — not scoped to country/province.
        b.HasIndex(x => new { x.TenantId, x.Abbreviation }).IsUnique();
        // "Unique within Country + Province."
        b.HasIndex(x => new { x.TenantId, x.CountryId, x.ProvinceState, x.CityName }).IsUnique();
    }
}

internal sealed class RouteMap : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> b)
    {
        b.ToTable("Routes");
        b.HasKey(x => x.RouteId);
        b.Property(x => x.RouteCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.RouteName).HasMaxLength(150).IsRequired();
        b.Property(x => x.DistanceKm).HasColumnType("decimal(9,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(1000);

        b.HasIndex(x => new { x.TenantId, x.RouteCode }).IsUnique();
    }
}

internal sealed class RouteStopMap : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> b)
    {
        b.ToTable("RouteStops");
        b.HasKey(x => x.RouteStopId);
        b.Property(x => x.StopType).HasMaxLength(12).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.RouteId, x.Sequence }).IsUnique();
    }
}

internal sealed class TripConfigurationMap : IEntityTypeConfiguration<TripConfiguration>
{
    public void Configure(EntityTypeBuilder<TripConfiguration> b)
    {
        b.ToTable("TripConfigurations");
        b.HasKey(x => x.TripConfigurationId);
        b.Property(x => x.TripCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.DirectionType).HasMaxLength(12).IsRequired();
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.TripCode }).IsUnique();
    }
}

internal sealed class TripConfigurationStopMap : IEntityTypeConfiguration<TripConfigurationStop>
{
    public void Configure(EntityTypeBuilder<TripConfigurationStop> b)
    {
        b.ToTable("TripConfigurationStops");
        b.HasKey(x => x.TripConfigurationStopId);
        b.Property(x => x.OtherLocation).HasMaxLength(200);
        b.Property(x => x.StopType).HasMaxLength(12).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.TripConfigurationId, x.Sequence }).IsUnique();
    }
}

internal sealed class TripConfigurationVehicleMap : IEntityTypeConfiguration<TripConfigurationVehicle>
{
    public void Configure(EntityTypeBuilder<TripConfigurationVehicle> b)
    {
        b.ToTable("TripConfigurationVehicles");
        b.HasKey(x => x.TripConfigurationVehicleId);
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripConfigurationId });
        b.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}

internal sealed class TripRateMap : IEntityTypeConfiguration<TripRate>
{
    public void Configure(EntityTypeBuilder<TripRate> b)
    {
        b.ToTable("TripRates");
        b.HasKey(x => x.TripRateId);
        b.Property(x => x.RateAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.TripConfigurationId });
        // "At most one open-ended Active rate per customer + configuration" (§26) — kept by the database too, not
        // only by the overlap-check/auto-close logic.
        b.HasIndex(x => x.TripConfigurationId).HasFilter("[EffectiveTo] IS NULL AND [Status] = 'Active'").IsUnique();
    }
}

internal sealed class TripMap : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("Trips");
        b.HasKey(x => x.TripId);
        b.Property(x => x.TripNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.TripType).HasMaxLength(10).IsRequired();
        b.Property(x => x.CustomerTripReference).HasMaxLength(50);
        b.Property(x => x.DriverOverrideReason).HasMaxLength(500);
        b.Property(x => x.StartOdometer).HasColumnType("decimal(10,1)");
        b.Property(x => x.EndOdometer).HasColumnType("decimal(10,1)");
        b.Property(x => x.TripRateAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RateSource).HasMaxLength(12).IsRequired();
        b.Property(x => x.TripAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.Property(x => x.FromLocationType).HasMaxLength(10);
        b.Property(x => x.FromOtherLocationType).HasMaxLength(20);
        b.Property(x => x.FromOtherLocationName).HasMaxLength(150);
        b.Property(x => x.ToLocationType).HasMaxLength(10);
        b.Property(x => x.ToOtherLocationType).HasMaxLength(20);
        b.Property(x => x.ToOtherLocationName).HasMaxLength(150);

        b.HasIndex(x => new { x.TenantId, x.TripNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasIndex(x => new { x.TenantId, x.TripConfigurationId });
        b.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}

internal sealed class TripStopMap : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> b)
    {
        b.ToTable("TripStops");
        b.HasKey(x => x.TripStopId);
        b.Property(x => x.LocationType).HasMaxLength(10).IsRequired();
        b.Property(x => x.OtherLocationType).HasMaxLength(20);
        b.Property(x => x.OtherLocationName).HasMaxLength(150);

        b.HasIndex(x => new { x.TenantId, x.TripId, x.Sequence }).IsUnique();
    }
}

internal sealed class TripNumberCounterMap : IEntityTypeConfiguration<TripNumberCounter>
{
    public void Configure(EntityTypeBuilder<TripNumberCounter> b)
    {
        b.ToTable("TripNumberCounters");
        b.HasKey(x => x.TripNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class TripEventMap : IEntityTypeConfiguration<TripEvent>
{
    public void Configure(EntityTypeBuilder<TripEvent> b)
    {
        b.ToTable("TripEvents");
        b.HasKey(x => x.TripEventId);
        b.Property(x => x.EventType).HasMaxLength(20).IsRequired();
        b.Property(x => x.LocationText).HasMaxLength(200);
        b.Property(x => x.Odometer).HasColumnType("decimal(10,1)");
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.Source).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.TripId, x.EventDateTime });
        // AC-54: an offline app's retried sync with the same ClientEventId never duplicates the event — kept by
        // the database too, not only by the app-level check (the same RecurringChargeGenerator/NotificationEvaluator idiom).
        b.HasIndex(x => new { x.TripId, x.ClientEventId }).HasFilter("[ClientEventId] IS NOT NULL").IsUnique();
    }
}

internal sealed class TripDocumentMap : IEntityTypeConfiguration<TripDocument>
{
    public void Configure(EntityTypeBuilder<TripDocument> b)
    {
        b.ToTable("TripDocuments");
        b.HasKey(x => x.TripDocumentId);
        b.Property(x => x.DocumentType).HasMaxLength(20).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.Source).HasMaxLength(10).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.TripId });
    }
}

internal sealed class TripPODMap : IEntityTypeConfiguration<TripPOD>
{
    public void Configure(EntityTypeBuilder<TripPOD> b)
    {
        b.ToTable("TripPODs");
        b.HasKey(x => x.TripPODId);
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.Source).HasMaxLength(10).IsRequired();
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.RejectedReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
    }
}

internal sealed class TripIssueMap : IEntityTypeConfiguration<TripIssue>
{
    public void Configure(EntityTypeBuilder<TripIssue> b)
    {
        b.ToTable("TripIssues");
        b.HasKey(x => x.TripIssueId);
        b.Property(x => x.IssueType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Severity).HasMaxLength(10).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ResolutionNotes).HasMaxLength(1000);

        b.HasIndex(x => new { x.TenantId, x.TripId });
    }
}

internal sealed class TripRateHistoryMap : IEntityTypeConfiguration<TripRateHistory>
{
    public void Configure(EntityTypeBuilder<TripRateHistory> b)
    {
        b.ToTable("TripRateHistories");
        b.HasKey(x => x.TripRateHistoryId);
        b.Property(x => x.OldRateAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.OldRateSource).HasMaxLength(12).IsRequired();
        b.Property(x => x.OldCurrencyCode).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.NewRateAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.NewRateSource).HasMaxLength(12).IsRequired();
        b.Property(x => x.NewCurrencyCode).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Action).HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
    }
}

internal sealed class FuelCardMap : IEntityTypeConfiguration<FuelCard>
{
    public void Configure(EntityTypeBuilder<FuelCard> b)
    {
        b.ToTable("FuelCards");
        b.HasKey(x => x.FuelCardId);
        b.Property(x => x.CardNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.CardHolderName).HasMaxLength(100);
        b.Property(x => x.MonthlyLimit).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        // §28: "Unique" — scoped to the tenant, the same convention every other master here uses.
        b.HasIndex(x => new { x.TenantId, x.CardNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.FuelCardCompanyId });
    }
}

internal sealed class FuelCardAssignmentMap : IEntityTypeConfiguration<FuelCardAssignment>
{
    public void Configure(EntityTypeBuilder<FuelCardAssignment> b)
    {
        b.ToTable("FuelCardAssignments");
        b.HasKey(x => x.FuelCardAssignmentId);
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.FuelCardId });
        // "No overlapping assignments per card" (§28) — the database-level backstop; the true guarantee is
        // structural (see FuelCardAssignment's own doc comment).
        b.HasIndex(x => x.FuelCardId).HasFilter("[AssignedTo] IS NULL").IsUnique();
    }
}

internal sealed class TripFuelMap : IEntityTypeConfiguration<TripFuel>
{
    public void Configure(EntityTypeBuilder<TripFuel> b)
    {
        b.ToTable("TripFuels");
        b.HasKey(x => x.TripFuelId);
        b.Property(x => x.FuelType).HasMaxLength(10).IsRequired();
        b.Property(x => x.Quantity).HasColumnType("decimal(10,3)");
        b.Property(x => x.Rate).HasColumnType("decimal(10,2)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Odometer).HasColumnType("decimal(10,1)");
        b.Property(x => x.StationName).HasMaxLength(150);
        b.Property(x => x.PaymentMethod).HasMaxLength(10).IsRequired();
        b.Property(x => x.OtherPaymentText).HasMaxLength(100);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Source).HasMaxLength(10).IsRequired();
        b.Property(x => x.VoidReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.FuelDateTime });
    }
}

internal sealed class TripExpenseMap : IEntityTypeConfiguration<TripExpense>
{
    public void Configure(EntityTypeBuilder<TripExpense> b)
    {
        b.ToTable("TripExpenses");
        b.HasKey(x => x.TripExpenseId);
        b.Property(x => x.OtherExpenseType).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        b.Property(x => x.Rate).HasColumnType("decimal(18,2)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reference).HasMaxLength(50);
        b.Property(x => x.PaymentMethod).HasMaxLength(15).IsRequired();
        b.Property(x => x.ApprovalStatus).HasMaxLength(10).IsRequired();
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.Source).HasMaxLength(10).IsRequired();
        b.Property(x => x.VoidReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
        b.HasIndex(x => new { x.TenantId, x.ApprovalStatus });
    }
}

internal sealed class TripIncomeMap : IEntityTypeConfiguration<TripIncome>
{
    public void Configure(EntityTypeBuilder<TripIncome> b)
    {
        b.ToTable("TripIncomes");
        b.HasKey(x => x.TripIncomeId);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Reference).HasMaxLength(50);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.VoidReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.IsBillable });
    }
}

internal sealed class InvoiceMap : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoices");
        b.HasKey(x => x.InvoiceId);
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.BillToAddressName).HasMaxLength(100);
        b.Property(x => x.BillToAddressLine1).HasMaxLength(200);
        b.Property(x => x.BillToAddressLine2).HasMaxLength(200);
        b.Property(x => x.BillToProvinceState).HasMaxLength(100);
        b.Property(x => x.BillToPostalCode).HasMaxLength(20);
        b.Property(x => x.BillToNtn).HasMaxLength(20);
        b.Property(x => x.BillToStrn).HasMaxLength(30);
        b.Property(x => x.CustomerCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Ntn).HasMaxLength(20);
        b.Property(x => x.Strn).HasMaxLength(30);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        foreach (var money in new[]
        {
            nameof(Invoice.TotalTripAmount), nameof(Invoice.TotalAdjustment), nameof(Invoice.GrossAmount), nameof(Invoice.TotalDeduction), nameof(Invoice.NetAmount),
            nameof(Invoice.PaidAmount), nameof(Invoice.AdvanceAppliedAmount), nameof(Invoice.WriteOffAmount), nameof(Invoice.DiscountAmount),
            nameof(Invoice.TransferredInAmount), nameof(Invoice.TransferredOutAmount), nameof(Invoice.CarryForwardInAmount), nameof(Invoice.CarryForwardOutAmount),
            nameof(Invoice.RefundedAmount), nameof(Invoice.BalanceAmount)
        })
            b.Property(money).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.PaymentStatus).HasMaxLength(15).IsRequired();
        b.Property(x => x.SubmissionChannel).HasMaxLength(10);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.RegenerationReason).HasMaxLength(500);
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.InvoiceNumber }).IsUnique();
        // §46.4: "IX (CustomerId, IsActive, PeriodFrom, PeriodTo) for overlap check."
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.IsActive, x.PeriodFrom, x.PeriodTo });
    }
}

internal sealed class InvoiceLineMap : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("InvoiceLines");
        b.HasKey(x => x.InvoiceLineId);
        b.Property(x => x.LineType).HasMaxLength(10).IsRequired();
        b.Property(x => x.TripNumber).HasMaxLength(20);
        b.Property(x => x.TripType).HasMaxLength(10);
        b.Property(x => x.CustomerTripReference).HasMaxLength(50);
        b.Property(x => x.RouteCode).HasMaxLength(30);
        b.Property(x => x.RouteLabel).HasMaxLength(300);
        b.Property(x => x.TripCode).HasMaxLength(40);
        b.Property(x => x.VehicleRegNo).HasMaxLength(20);
        b.Property(x => x.DriverName).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        b.Property(x => x.Rate).HasColumnType("decimal(18,2)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RateSource).HasMaxLength(12);
        b.Property(x => x.CustomerCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();

        // §46.5: "IX InvoiceLine(InvoiceId)" for report performance.
        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        b.HasIndex(x => new { x.TenantId, x.TripId });
    }
}

internal sealed class InvoiceTripLinkMap : IEntityTypeConfiguration<InvoiceTripLink>
{
    public void Configure(EntityTypeBuilder<InvoiceTripLink> b)
    {
        b.ToTable("InvoiceTripLinks");
        b.HasKey(x => x.InvoiceTripLinkId);
        b.Property(x => x.UnlinkReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        // §32.1/§46.4/§46.5: "UX_InvoiceTripLink_ActiveTrip ON (TripId) WHERE IsActive = 1" — the database-level
        // guarantee a trip is billed on at most one active invoice. Not compounded with TenantId, matching this
        // module's existing filtered-unique-index idiom (TripRate, FuelCardAssignment): the referenced id is
        // already tenant-scoped, since ids never collide across tenants.
        b.HasIndex(x => x.TripId).HasFilter("[IsActive] = 1").IsUnique();
    }
}

internal sealed class InvoiceAdjustmentMap : IEntityTypeConfiguration<InvoiceAdjustment>
{
    public void Configure(EntityTypeBuilder<InvoiceAdjustment> b)
    {
        b.ToTable("InvoiceAdjustments");
        b.HasKey(x => x.InvoiceAdjustmentId);
        b.Property(x => x.AdjustmentMonth).HasMaxLength(30).IsRequired();
        b.Property(x => x.AdjustmentAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.AdjustmentNote).HasMaxLength(500).IsRequired();
        b.Property(x => x.ReferenceInvoiceNo).HasMaxLength(30);

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

internal sealed class InvoiceTaxLineMap : IEntityTypeConfiguration<InvoiceTaxLine>
{
    public void Configure(EntityTypeBuilder<InvoiceTaxLine> b)
    {
        b.ToTable("InvoiceTaxLines");
        b.HasKey(x => x.InvoiceTaxLineId);
        b.Property(x => x.TaxName).HasMaxLength(100).IsRequired();
        b.Property(x => x.TaxCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.TaxType).HasMaxLength(12).IsRequired();
        b.Property(x => x.TaxPercentage).HasColumnType("decimal(9,4)");
        b.Property(x => x.FixedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CalculationBasis).HasMaxLength(20).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

internal sealed class InvoiceHistoryMap : IEntityTypeConfiguration<InvoiceHistory>
{
    public void Configure(EntityTypeBuilder<InvoiceHistory> b)
    {
        b.ToTable("InvoiceHistories");
        b.HasKey(x => x.InvoiceHistoryId);
        b.Property(x => x.FromStatus).HasMaxLength(10).IsRequired();
        b.Property(x => x.ToStatus).HasMaxLength(10).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

internal sealed class InvoiceReplacementMap : IEntityTypeConfiguration<InvoiceReplacement>
{
    public void Configure(EntityTypeBuilder<InvoiceReplacement> b)
    {
        b.ToTable("InvoiceReplacements");
        b.HasKey(x => x.InvoiceReplacementId);
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.OldInvoiceId });
        b.HasIndex(x => new { x.TenantId, x.NewInvoiceId });
    }
}

internal sealed class InvoiceNumberCounterMap : IEntityTypeConfiguration<InvoiceNumberCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberCounter> b)
    {
        b.ToTable("InvoiceNumberCounters");
        b.HasKey(x => x.InvoiceNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class InvoiceDocumentMap : IEntityTypeConfiguration<InvoiceDocument>
{
    public void Configure(EntityTypeBuilder<InvoiceDocument> b)
    {
        b.ToTable("InvoiceDocuments");
        b.HasKey(x => x.InvoiceDocumentId);
        b.Property(x => x.DocumentType).HasMaxLength(20).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        // "Re-print returns the stored file" — at most one current version per invoice.
        b.HasIndex(x => x.InvoiceId).HasFilter("[IsCurrent] = 1").IsUnique();
    }
}

internal sealed class InvoiceEvidenceMap : IEntityTypeConfiguration<InvoiceEvidence>
{
    public void Configure(EntityTypeBuilder<InvoiceEvidence> b)
    {
        b.ToTable("InvoiceEvidences");
        b.HasKey(x => x.InvoiceEvidenceId);
        b.Property(x => x.GroupBy).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        // §41: several versions genuinely coexist (a re-render never removes the old one) — no "one current
        // row" filtered index like InvoiceDocument's, only "one row per (InvoiceId, EvidenceVersion)."
        b.HasIndex(x => new { x.InvoiceId, x.EvidenceVersion }).IsUnique();
    }
}

internal sealed class CustomerLedgerEntryMap : IEntityTypeConfiguration<CustomerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerEntry> b)
    {
        // §40A: append-only — the same INSTEAD OF UPDATE, DELETE trigger idiom InvoiceLine (CC-23) and
        // core.AuditEntries already use; a DB permission grant alone depends on which login connects, a trigger
        // holds regardless. The trigger itself is added by raw SQL in the migration, not expressible here.
        b.ToTable("CustomerLedgerEntries", tb => tb.HasCheckConstraint(
            "CK_CustomerLedgerEntries_OneOfDebitCredit",
            "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)"));
        b.HasKey(x => x.CustomerLedgerEntryId);
        b.Property(x => x.EntryNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.EntryType).HasMaxLength(30).IsRequired();
        b.Property(x => x.DebitAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CreditAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
        b.Property(x => x.DocumentNo).HasMaxLength(30).IsRequired();
        b.Property(x => x.Narration).HasMaxLength(300).IsRequired();

        // Unique per tenant, not globally — EntryNumber is allocated per-tenant-per-year (LedgerNumberCounters),
        // exactly like Invoice.InvoiceNumber/Trip.TripNumber; two different tenants legitimately reuse the same
        // number, since every query is already tenant-filtered.
        b.HasIndex(x => new { x.TenantId, x.EntryNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.CustomerSeq });
        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        // §40A.2: "the same event can never post twice (idempotent re-submit, double-click safe)." Not
        // compounded with TenantId — SourceId (an identity column) is already globally unique regardless of
        // tenant, the same reasoning TripRate/FuelCardAssignment/InvoiceTripLink's own unique indexes rely on.
        b.HasIndex(x => new { x.SourceType, x.SourceId, x.EntryType }).IsUnique();
    }
}

internal sealed class CustomerBalanceMap : IEntityTypeConfiguration<CustomerBalance>
{
    public void Configure(EntityTypeBuilder<CustomerBalance> b)
    {
        b.ToTable("CustomerBalances");
        b.HasKey(x => x.CustomerBalanceId);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.BalanceAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RowVersion).IsRowVersion();

        // §40A.2/LR-9: one balance row per customer per currency.
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.CurrencyCode }).IsUnique();
    }
}

internal sealed class BankCashAccountMap : IEntityTypeConfiguration<BankCashAccount>
{
    public void Configure(EntityTypeBuilder<BankCashAccount> b)
    {
        b.ToTable("BankCashAccounts");
        b.HasKey(x => x.BankCashAccountId);
        b.Property(x => x.AccountTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.BankName).HasMaxLength(200).IsRequired();
        b.Property(x => x.BranchName).HasMaxLength(200).IsRequired();
        b.Property(x => x.AccountNumberLast4).HasMaxLength(4).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class CustomerReceiptMap : IEntityTypeConfiguration<CustomerReceipt>
{
    public void Configure(EntityTypeBuilder<CustomerReceipt> b)
    {
        b.ToTable("CustomerReceipts");
        b.HasKey(x => x.CustomerReceiptId);
        b.Property(x => x.ReceiptNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.ReceiptType).HasMaxLength(20).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
        b.Property(x => x.InstrumentNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.DrawnOnBank).HasMaxLength(100);
        b.Property(x => x.PaymentReference).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        // BR-P4's own duplicate-guard read (customer + instrument + amount + method).
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.InstrumentNo });
    }
}

internal sealed class InvoicePaymentMap : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> b)
    {
        b.ToTable("InvoicePayments");
        b.HasKey(x => x.InvoicePaymentId);
        b.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
        b.Property(x => x.PaymentReference).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.ReversalReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
        b.HasIndex(x => new { x.TenantId, x.CustomerReceiptId });
    }
}

internal sealed class InvoiceSettlementMap : IEntityTypeConfiguration<InvoiceSettlement>
{
    public void Configure(EntityTypeBuilder<InvoiceSettlement> b)
    {
        b.ToTable("InvoiceSettlements");
        b.HasKey(x => x.InvoiceSettlementId);
        b.Property(x => x.SettlementNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.SettlementType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ReversalReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

internal sealed class SettlementNumberCounterMap : IEntityTypeConfiguration<SettlementNumberCounter>
{
    public void Configure(EntityTypeBuilder<SettlementNumberCounter> b)
    {
        b.ToTable("SettlementNumberCounters");
        b.HasKey(x => x.SettlementNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class CustomerAdvanceMap : IEntityTypeConfiguration<CustomerAdvance>
{
    public void Configure(EntityTypeBuilder<CustomerAdvance> b)
    {
        b.ToTable("CustomerAdvances");
        b.HasKey(x => x.CustomerAdvanceId);
        b.Property(x => x.AdvanceNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.AppliedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RefundedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.MoveReason).HasMaxLength(500);
        b.Property(x => x.RefundReason).HasMaxLength(500);
        b.Property(x => x.ReversalReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.TripId });
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
    }
}

internal sealed class AdvanceNumberCounterMap : IEntityTypeConfiguration<AdvanceNumberCounter>
{
    public void Configure(EntityTypeBuilder<AdvanceNumberCounter> b)
    {
        b.ToTable("AdvanceNumberCounters");
        b.HasKey(x => x.AdvanceNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class InvoicePaymentTransferMap : IEntityTypeConfiguration<InvoicePaymentTransfer>
{
    public void Configure(EntityTypeBuilder<InvoicePaymentTransfer> b)
    {
        b.ToTable("InvoicePaymentTransfers");
        b.HasKey(x => x.InvoicePaymentTransferId);
        b.Property(x => x.TransferNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.SourceKind).HasMaxLength(30).IsRequired();
        b.Property(x => x.AmountTransferred).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.OldInvoiceId });
        b.HasIndex(x => new { x.TenantId, x.NewInvoiceId });
    }
}

internal sealed class TransferNumberCounterMap : IEntityTypeConfiguration<TransferNumberCounter>
{
    public void Configure(EntityTypeBuilder<TransferNumberCounter> b)
    {
        b.ToTable("TransferNumberCounters");
        b.HasKey(x => x.TransferNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class InvoiceCreditCarryForwardMap : IEntityTypeConfiguration<InvoiceCreditCarryForward>
{
    public void Configure(EntityTypeBuilder<InvoiceCreditCarryForward> b)
    {
        b.ToTable("InvoiceCreditCarryForwards");
        b.HasKey(x => x.InvoiceCreditCarryForwardId);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.SourceInvoiceId });
        b.HasIndex(x => new { x.TenantId, x.TargetInvoiceId });
    }
}

internal sealed class CustomerRefundMap : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> b)
    {
        b.ToTable("CustomerRefunds");
        b.HasKey(x => x.CustomerRefundId);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
        b.Property(x => x.PaymentReference).HasMaxLength(100);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

internal sealed class LedgerReconciliationRunMap : IEntityTypeConfiguration<LedgerReconciliationRun>
{
    public void Configure(EntityTypeBuilder<LedgerReconciliationRun> b)
    {
        b.ToTable("LedgerReconciliationRuns");
        b.HasKey(x => x.LedgerReconciliationRunId);
        b.HasIndex(x => new { x.TenantId, x.RunOn });
    }
}

internal sealed class LedgerReconciliationMismatchMap : IEntityTypeConfiguration<LedgerReconciliationMismatch>
{
    public void Configure(EntityTypeBuilder<LedgerReconciliationMismatch> b)
    {
        b.ToTable("LedgerReconciliationMismatches");
        b.HasKey(x => x.LedgerReconciliationMismatchId);
        b.Property(x => x.LedgerBalance).HasColumnType("decimal(18,2)");
        b.Property(x => x.InvoiceBalance).HasColumnType("decimal(18,2)");
        b.Property(x => x.Difference).HasColumnType("decimal(18,2)");

        b.HasIndex(x => new { x.TenantId, x.LedgerReconciliationRunId });
    }
}

internal sealed class LedgerPeriodMap : IEntityTypeConfiguration<LedgerPeriod>
{
    public void Configure(EntityTypeBuilder<LedgerPeriod> b)
    {
        b.ToTable("LedgerPeriods");
        b.HasKey(x => x.LedgerPeriodId);
        b.Property(x => x.YearMonth).HasMaxLength(6).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.CloseReason).HasMaxLength(500);
        b.Property(x => x.ReopenReason).HasMaxLength(500);

        b.HasIndex(x => new { x.TenantId, x.YearMonth }).IsUnique();
    }
}

internal sealed class ReportExportJobMap : IEntityTypeConfiguration<ReportExportJob>
{
    public void Configure(EntityTypeBuilder<ReportExportJob> b)
    {
        b.ToTable("ReportExportJobs");
        b.HasKey(x => x.ReportExportJobId);
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.FiltersJson).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(300);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.OriginalFileName).HasMaxLength(260);
        b.Property(x => x.RequestedByName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);

        b.HasIndex(x => new { x.TenantId, x.Status });
    }
}

internal sealed class ReceiptNumberCounterMap : IEntityTypeConfiguration<ReceiptNumberCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptNumberCounter> b)
    {
        b.ToTable("ReceiptNumberCounters");
        b.HasKey(x => x.ReceiptNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class LedgerNumberCounterMap : IEntityTypeConfiguration<LedgerNumberCounter>
{
    public void Configure(EntityTypeBuilder<LedgerNumberCounter> b)
    {
        b.ToTable("LedgerNumberCounters");
        b.HasKey(x => x.LedgerNumberCounterId);
        b.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
    }
}

internal sealed class CustomerLedgerSequenceCounterMap : IEntityTypeConfiguration<CustomerLedgerSequenceCounter>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerSequenceCounter> b)
    {
        b.ToTable("CustomerLedgerSequenceCounters");
        b.HasKey(x => x.CustomerLedgerSequenceCounterId);
        b.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
    }
}

internal sealed class ExchangeRateMap : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("ExchangeRates");
        b.HasKey(x => x.ExchangeRateId);
        b.Property(x => x.FromCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ToCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Rate).HasColumnType("decimal(18,6)");   // rates need more than 2dp precision; amounts derived from them are still rounded to 2dp

        b.HasIndex(x => new { x.TenantId, x.FromCurrencyCode, x.ToCurrencyCode, x.RateDate }).IsUnique();
    }
}
