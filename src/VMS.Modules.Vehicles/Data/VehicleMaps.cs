using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Vehicles.Domain;

namespace VMS.Modules.Vehicles.Data;

internal sealed class VehicleMap : IEntityTypeConfiguration<Vehicle>
{
    // SQL Server cannot filter an index on a computed column, so the filters spell the same rule out on the plain columns.
    private const string Live = "[Status] <> 'Sold' AND [Status] <> 'Transferred' AND [IsDeleted] = 0";
    private const string InFleet = "[Status] IN ('Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable') AND [IsDeleted] = 0";

    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("Vehicles");
        b.HasKey(x => x.VehicleId);
        b.Property(x => x.VehicleCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.RegistrationNo).HasMaxLength(20).IsRequired();
        b.Property(x => x.RegNoKey).HasMaxLength(20).IsRequired();
        b.Property(x => x.ChassisNo).HasMaxLength(30);
        b.Property(x => x.EngineNo).HasMaxLength(30);
        b.Property(x => x.Model).HasMaxLength(60).IsRequired();
        b.Property(x => x.Colour).HasMaxLength(30);
        b.Property(x => x.FuelType).HasMaxLength(10).IsRequired();
        b.Property(x => x.TankCapacity).HasColumnType("decimal(10,2)");
        b.Property(x => x.LoadCapacity).HasColumnType("decimal(10,2)");
        b.Property(x => x.CapacityUnit).HasMaxLength(12);
        b.Property(x => x.Gvw).HasColumnType("decimal(10,2)");
        b.Property(x => x.FuelCardNumber).HasMaxLength(30);
        b.Property(x => x.TrackerDeviceId).HasMaxLength(40);
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.AcquisitionType).HasMaxLength(20);
        b.Property(x => x.Status).HasMaxLength(24).IsRequired();
        b.Property(x => x.CurrentCategory).HasMaxLength(20);
        b.Property(x => x.DraftData).HasColumnType("nvarchar(max)");
        b.Property(x => x.RowVersion).IsRowVersion();

        // Two facts queries use (the unique indexes below spell the same rules out on the plain columns), kept by the database so no code path can forget them.
        b.Property(x => x.IsLive).HasComputedColumnSql("CAST(CASE WHEN [Status] IN ('Sold', 'Transferred') OR [IsDeleted] = 1 THEN 0 ELSE 1 END AS bit)", stored: true);
        b.Property(x => x.IsInFleet).HasComputedColumnSql("CAST(CASE WHEN [Status] IN ('Active', 'Assigned', 'UnderMaintenance', 'TemporarilyUnavailable') AND [IsDeleted] = 0 THEN 1 ELSE 0 END AS bit)", stored: true);

        b.HasIndex(x => new { x.TenantId, x.VehicleCode }).IsUnique();
        // BR-VH-017: a registration number is unique unless its vehicle was sold or transferred (a re-registered vehicle updates its own number).
        b.HasIndex(x => new { x.TenantId, x.RegNoKey }).IsUnique().HasFilter(Live).HasDatabaseName("UX_Vehicles_RegNo");
        b.HasIndex(x => new { x.TenantId, x.ChassisNo }).IsUnique().HasFilter($"[ChassisNo] IS NOT NULL AND {Live}").HasDatabaseName("UX_Vehicles_Chassis");
        b.HasIndex(x => new { x.TenantId, x.EngineNo }).IsUnique().HasFilter($"[EngineNo] IS NOT NULL AND {Live}").HasDatabaseName("UX_Vehicles_Engine");
        // BR-VH-026: a fuel card number is on one vehicle in the fleet at a time.
        b.HasIndex(x => new { x.TenantId, x.FuelCardNumber }).IsUnique().HasFilter($"[FuelCardNumber] IS NOT NULL AND {InFleet}").HasDatabaseName("UX_Vehicles_FuelCard");

        b.HasIndex(x => new { x.TenantId, x.Status, x.ModifiedOn });
        b.HasIndex(x => new { x.TenantId, x.VehicleTypeId });
        b.HasIndex(x => new { x.TenantId, x.CurrentCategory });
        b.HasIndex(x => new { x.TenantId, x.DefaultDriverId });
    }
}

internal sealed class VehicleRelationMap : IEntityTypeConfiguration<VehicleRelation>
{
    public void Configure(EntityTypeBuilder<VehicleRelation> b)
    {
        b.ToTable("VehicleRelations");
        b.HasKey(x => x.VehicleRelationId);
        b.Property(x => x.Category).HasMaxLength(20).IsRequired();
        b.Property(x => x.AgreementReference).HasMaxLength(60);
        b.Property(x => x.SharePercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.SharingBasis).HasMaxLength(20);
        b.Property(x => x.FixedMonthlyAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.ExpenseSharingRule).HasMaxLength(24);
        b.Property(x => x.RentAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RentFrequency).HasMaxLength(10);
        b.Property(x => x.SecurityDeposit).HasColumnType("decimal(18,2)");
        b.Property(x => x.ArrangementType).HasMaxLength(20);
        b.Property(x => x.AgreedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RevenueSharePercent).HasColumnType("decimal(5,2)");

        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-002: one open relation at a time.
        b.HasIndex(x => new { x.TenantId, x.VehicleId }).IsUnique().HasFilter("[EffectiveTo] IS NULL").HasDatabaseName("UX_VehicleRelations_Open");
        b.HasIndex(x => new { x.TenantId, x.CounterpartyId });
    }
}

internal sealed class VehicleLifecycleEntryMap : IEntityTypeConfiguration<VehicleLifecycleEntry>
{
    public void Configure(EntityTypeBuilder<VehicleLifecycleEntry> b)
    {
        b.ToTable("VehicleLifecycle");
        b.HasKey(x => x.VehicleLifecycleEntryId);
        b.Property(x => x.EventType).HasMaxLength(20).IsRequired();
        b.Property(x => x.FromStatus).HasMaxLength(24);
        b.Property(x => x.ToStatus).HasMaxLength(24);
        b.Property(x => x.FromCategory).HasMaxLength(20);
        b.Property(x => x.ToCategory).HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.UserName).HasMaxLength(200);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.OccurredOn });
    }
}

internal sealed class VehicleAttachedItemMap : IEntityTypeConfiguration<VehicleAttachedItem>
{
    public void Configure(EntityTypeBuilder<VehicleAttachedItem> b)
    {
        b.ToTable("VehicleAttachedItems");
        b.HasKey(x => x.VehicleAttachedItemId);
        b.Property(x => x.Description).HasMaxLength(150).IsRequired();
        b.Property(x => x.SerialNo).HasMaxLength(60);
        b.Property(x => x.Cost).HasColumnType("decimal(18,2)");
        b.Property(x => x.Condition).HasMaxLength(12);
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.Property(x => x.DetachReason).HasMaxLength(500);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-014: a serial number is on one attached item at a time; it may come back after the earlier one is detached.
        b.HasIndex(x => new { x.TenantId, x.SerialNo }).IsUnique().HasFilter("[SerialNo] IS NOT NULL AND [Status] = 'Attached'").HasDatabaseName("UX_VehicleItems_Serial");
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.Status });
    }
}

internal sealed class OdometerReadingMap : IEntityTypeConfiguration<OdometerReading>
{
    public void Configure(EntityTypeBuilder<OdometerReading> b)
    {
        b.ToTable("OdometerReadings");
        b.HasKey(x => x.OdometerReadingId);
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(250);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.ReadingDate });
    }
}

internal sealed class DriverAssignmentMap : IEntityTypeConfiguration<DriverAssignment>
{
    public void Configure(EntityTypeBuilder<DriverAssignment> b)
    {
        b.ToTable("DriverAssignments");
        b.HasKey(x => x.DriverAssignmentId);
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-022: a vehicle has one default driver, and a driver is the default driver of one vehicle, at a time.
        b.HasIndex(x => new { x.TenantId, x.VehicleId }).IsUnique().HasFilter("[EffectiveTo] IS NULL").HasDatabaseName("UX_DriverAssignments_Vehicle");
        b.HasIndex(x => new { x.TenantId, x.DriverId }).IsUnique().HasFilter("[EffectiveTo] IS NULL").HasDatabaseName("UX_DriverAssignments_Driver");
    }
}

internal sealed class VehicleAcquisitionMap : IEntityTypeConfiguration<VehicleAcquisition>
{
    public void Configure(EntityTypeBuilder<VehicleAcquisition> b)
    {
        b.ToTable("VehicleAcquisitions");
        b.HasKey(x => x.VehicleId);
        b.Property(x => x.VehicleId).ValueGeneratedNever();
        b.Property(x => x.PurchasePrice).HasColumnType("decimal(18,2)");
        b.Property(x => x.AmountPaid).HasColumnType("decimal(18,2)");
        b.Property(x => x.PaymentMode).HasMaxLength(12);
        b.Property(x => x.PaymentReference).HasMaxLength(60);
        b.Property(x => x.RegistrationCost).HasColumnType("decimal(18,2)");
        b.HasOne<Vehicle>().WithOne().HasForeignKey<VehicleAcquisition>(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VehicleTransactionMap : IEntityTypeConfiguration<VehicleTransaction>
{
    public void Configure(EntityTypeBuilder<VehicleTransaction> b)
    {
        b.ToTable("VehicleTransactions");
        b.HasKey(x => x.VehicleTransactionId);
        b.Property(x => x.Type).HasMaxLength(20).IsRequired();
        b.Property(x => x.SubType).HasMaxLength(40);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Reference).HasMaxLength(120);
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ReceiptStorageKey).HasMaxLength(300);
        b.Property(x => x.ReceiptSha256).HasMaxLength(64);
        b.Property(x => x.ReceiptContentType).HasMaxLength(100);
        b.Property(x => x.ReceiptFileName).HasMaxLength(200);
        b.HasOne<VehicleTransaction>().WithMany().HasForeignKey(x => x.ReversesTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VehicleInstallment>().WithMany().HasForeignKey(x => x.InstallmentId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-011: an entry is reversed once. The database refuses a second reversal, whatever two people do at the same moment.
        b.HasIndex(x => x.ReversesTransactionId).IsUnique().HasFilter("[ReversesTransactionId] IS NOT NULL").HasDatabaseName("UX_VehicleTransactions_Reversal");
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.TransactionDate });
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.Type });
    }
}

internal sealed class VehicleFinanceAgreementMap : IEntityTypeConfiguration<VehicleFinanceAgreement>
{
    public void Configure(EntityTypeBuilder<VehicleFinanceAgreement> b)
    {
        b.ToTable("VehicleFinanceAgreements");
        b.HasKey(x => x.VehicleFinanceAgreementId);
        b.Property(x => x.AgreementNo).HasMaxLength(60).IsRequired();
        b.Property(x => x.FinanceAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.DownPayment).HasColumnType("decimal(18,2)");
        b.Property(x => x.InstallmentAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Frequency).HasMaxLength(12).IsRequired();
        b.Property(x => x.MarkupRate).HasColumnType("decimal(5,2)");
        b.Property(x => x.ResidualAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.SecurityDeposit).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-004: a vehicle has at most one open agreement.
        b.HasIndex(x => new { x.TenantId, x.VehicleId }).IsUnique().HasFilter("[Status] IN ('Draft', 'Active')").HasDatabaseName("UX_VehicleFinance_Open");
        // An agreement number is unique for a bank, settled ones included: the bank's own reference is never reused.
        b.HasIndex(x => new { x.TenantId, x.BankId, x.AgreementNo }).IsUnique().HasDatabaseName("UX_VehicleFinance_BankAgreement");
    }
}

internal sealed class VehicleInstallmentMap : IEntityTypeConfiguration<VehicleInstallment>
{
    public void Configure(EntityTypeBuilder<VehicleInstallment> b)
    {
        b.ToTable("VehicleInstallments");
        b.HasKey(x => x.VehicleInstallmentId);
        b.Property(x => x.ExpectedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.PaidAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(14).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<VehicleFinanceAgreement>().WithMany().HasForeignKey(x => x.VehicleFinanceAgreementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.VehicleFinanceAgreementId, x.InstallmentNo }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.DueDate });
    }
}

internal sealed class VehicleRecurringChargeMap : IEntityTypeConfiguration<VehicleRecurringCharge>
{
    public void Configure(EntityTypeBuilder<VehicleRecurringCharge> b)
    {
        b.ToTable("VehicleRecurringCharges");
        b.HasKey(x => x.VehicleRecurringChargeId);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.AmountBasis).HasMaxLength(20).IsRequired();
        b.Property(x => x.Frequency).HasMaxLength(12).IsRequired();
        b.Property(x => x.PostingMode).HasMaxLength(14).IsRequired();
        b.Property(x => x.TaxWithholdingPercent).HasColumnType("decimal(5,2)");
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-033: one row of a series is in force at a time.
        b.HasIndex(x => new { x.TenantId, x.SeriesId }).IsUnique().HasFilter("[EffectiveTo] IS NULL").HasDatabaseName("UX_VehicleRecurringCharges_Open");
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.EffectiveTo });
        b.HasIndex(x => new { x.TenantId, x.NextDueDate });
    }
}

internal sealed class VehicleRecurringChargeEntryMap : IEntityTypeConfiguration<VehicleRecurringChargeEntry>
{
    public void Configure(EntityTypeBuilder<VehicleRecurringChargeEntry> b)
    {
        b.ToTable("VehicleRecurringChargeEntries");
        b.HasKey(x => x.VehicleRecurringChargeEntryId);
        b.Property(x => x.PeriodKey).HasMaxLength(20).IsRequired();
        b.Property(x => x.ExpectedAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasMaxLength(10).IsRequired();
        b.Property(x => x.PaidAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.PaymentMode).HasMaxLength(12);
        b.Property(x => x.Reference).HasMaxLength(120);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.WaiveReason).HasMaxLength(500);
        b.HasOne<VehicleRecurringCharge>().WithMany().HasForeignKey(x => x.VehicleRecurringChargeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VehicleTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        // BR-VH-030: the generation job never creates two entries for the same charge and period.
        b.HasIndex(x => new { x.VehicleRecurringChargeId, x.PeriodKey }).IsUnique().HasDatabaseName("UX_VehicleRecurringChargeEntries_Period");
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.Status, x.DueDate });
    }
}