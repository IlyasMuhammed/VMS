using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.BusinessPartners.Domain;

namespace VMS.Modules.BusinessPartners.Data;

internal sealed class BusinessPartnerMap : IEntityTypeConfiguration<BusinessPartner>
{
    // A partner "counts" for uniqueness unless it was merged into another or soft-deleted (BR-BP-013, BR-BP-015).
    private const string Live = "[Status] <> 'Merged' AND [IsDeleted] = 0";

    public void Configure(EntityTypeBuilder<BusinessPartner> b)
    {
        b.ToTable("BusinessPartners");
        b.HasKey(x => x.BusinessPartnerId);
        b.Property(x => x.BpCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.PartyType).HasMaxLength(10).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(150).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(60);
        b.Property(x => x.Cnic).HasMaxLength(15);
        b.Property(x => x.Ntn).HasMaxLength(15);
        b.Property(x => x.Strn).HasMaxLength(20);
        b.Property(x => x.FilerStatus).HasMaxLength(10).IsRequired();
        b.Property(x => x.PrimaryMobile).HasMaxLength(20).IsRequired();
        b.Property(x => x.AlternatePhone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(100);
        b.Property(x => x.AddressLine).HasMaxLength(250).IsRequired();
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)");
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.BpCode }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Cnic }).IsUnique().HasFilter($"[Cnic] IS NOT NULL AND {Live}").HasDatabaseName("UX_BusinessPartners_Cnic");
        b.HasIndex(x => new { x.TenantId, x.Ntn }).IsUnique().HasFilter($"[Ntn] IS NOT NULL AND [PartyType] = 'Company' AND {Live}").HasDatabaseName("UX_BusinessPartners_Ntn");
        b.HasIndex(x => new { x.TenantId, x.Strn }).IsUnique().HasFilter("[Strn] IS NOT NULL AND [IsDeleted] = 0").HasDatabaseName("UX_BusinessPartners_Strn");

        // What the list and the pickers search and sort on.
        b.HasIndex(x => new { x.TenantId, x.LegalName });
        b.HasIndex(x => new { x.TenantId, x.PrimaryMobile });
        b.HasIndex(x => new { x.TenantId, x.Status, x.ModifiedOn });
        b.HasIndex(x => new { x.TenantId, x.CityId });

        b.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Contacts).WithOne().HasForeignKey(r => r.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Addresses).WithOne().HasForeignKey(r => r.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.BankAccounts).WithOne().HasForeignKey(r => r.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Driver).WithOne().HasForeignKey<BpDriverDetail>(d => d.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Vendor).WithOne().HasForeignKey<BpVendorDetail>(d => d.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Customer).WithOne().HasForeignKey<BpCustomerDetail>(d => d.BusinessPartnerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BusinessPartnerRoleMap : IEntityTypeConfiguration<BusinessPartnerRole>
{
    public void Configure(EntityTypeBuilder<BusinessPartnerRole> b)
    {
        b.ToTable("BusinessPartnerRoles");
        b.HasKey(x => x.BpRoleId);
        b.Property(x => x.RoleCode).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.BusinessPartnerId, x.RoleCode }).IsUnique();   // one row per role ever held; re-adding reactivates it
        b.HasIndex(x => new { x.TenantId, x.RoleCode, x.IsActive });
    }
}

internal sealed class BpDriverDetailMap : IEntityTypeConfiguration<BpDriverDetail>
{
    public void Configure(EntityTypeBuilder<BpDriverDetail> b)
    {
        b.ToTable("BpDriverDetails");
        b.HasKey(x => x.BusinessPartnerId);
        b.Property(x => x.BusinessPartnerId).ValueGeneratedNever();
        b.Property(x => x.LicenceNo).HasMaxLength(30).IsRequired();
        b.Property(x => x.LicenceType).HasMaxLength(12).IsRequired();
        b.Property(x => x.EmploymentType).HasMaxLength(12).IsRequired();
        b.Property(x => x.MonthlyRate).HasColumnType("decimal(18,2)");
        b.Property(x => x.CommissionBasis).HasMaxLength(10).IsRequired();
        b.Property(x => x.CommissionValue).HasColumnType("decimal(18,2)");
        b.Property(x => x.BloodGroup).HasMaxLength(5);
        b.Property(x => x.EmergencyContactName).HasMaxLength(100);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
        b.Property(x => x.Guarantor).HasMaxLength(150);
        b.HasIndex(x => new { x.TenantId, x.LicenceNo });
    }
}

internal sealed class BpVendorDetailMap : IEntityTypeConfiguration<BpVendorDetail>
{
    public void Configure(EntityTypeBuilder<BpVendorDetail> b)
    {
        b.ToTable("BpVendorDetails");
        b.HasKey(x => x.BusinessPartnerId);
        b.Property(x => x.BusinessPartnerId).ValueGeneratedNever();
        b.Property(x => x.SupplyCategories).HasMaxLength(200).IsRequired();
        b.Property(x => x.CreditLimit).HasColumnType("decimal(18,2)");
    }
}

internal sealed class BpCustomerDetailMap : IEntityTypeConfiguration<BpCustomerDetail>
{
    public void Configure(EntityTypeBuilder<BpCustomerDetail> b)
    {
        b.ToTable("BpCustomerDetails");
        b.HasKey(x => x.BusinessPartnerId);
        b.Property(x => x.BusinessPartnerId).ValueGeneratedNever();
        b.Property(x => x.CustomerType).HasMaxLength(15).IsRequired();
        b.Property(x => x.BillingCycle).HasMaxLength(15).IsRequired();
        b.Property(x => x.RateBasis).HasMaxLength(15);
        b.Property(x => x.DefaultRate).HasColumnType("decimal(18,2)");
        b.Property(x => x.CreditLimit).HasColumnType("decimal(18,2)");
    }
}

internal sealed class BpContactMap : IEntityTypeConfiguration<BpContact>
{
    public void Configure(EntityTypeBuilder<BpContact> b)
    {
        b.ToTable("BpContacts");
        b.HasKey(x => x.BpContactId);
        b.Property(x => x.ContactName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Designation).HasMaxLength(60);
        b.Property(x => x.Mobile).HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(250);
        // BR-BP-002: the database itself allows one primary per partner.
        b.HasIndex(x => x.BusinessPartnerId).IsUnique().HasFilter("[IsPrimary] = 1").HasDatabaseName("UX_BpContacts_Primary");
    }
}

internal sealed class BpAddressMap : IEntityTypeConfiguration<BpAddress>
{
    public void Configure(EntityTypeBuilder<BpAddress> b)
    {
        b.ToTable("BpAddresses");
        b.HasKey(x => x.BpAddressId);
        b.Property(x => x.AddressType).HasMaxLength(15).IsRequired();
        b.Property(x => x.Line1).HasMaxLength(250).IsRequired();
        b.Property(x => x.Line2).HasMaxLength(250);
        b.Property(x => x.ProvinceCode).HasMaxLength(30);
        b.Property(x => x.Landmark).HasMaxLength(150);
        b.HasIndex(x => x.BusinessPartnerId).IsUnique().HasFilter("[IsPrimary] = 1").HasDatabaseName("UX_BpAddresses_Primary");
    }
}

internal sealed class BpBankAccountMap : IEntityTypeConfiguration<BpBankAccount>
{
    public void Configure(EntityTypeBuilder<BpBankAccount> b)
    {
        b.ToTable("BpBankAccounts");
        b.HasKey(x => x.BpBankAccountId);
        b.Property(x => x.AccountTitle).HasMaxLength(150).IsRequired();
        b.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        b.Property(x => x.BranchCode).HasMaxLength(80);
        b.Property(x => x.AccountNumber).HasMaxLength(34).IsRequired();
        b.Property(x => x.Iban).HasMaxLength(34);
        b.HasIndex(x => x.BusinessPartnerId).IsUnique().HasFilter("[IsPrimary] = 1").HasDatabaseName("UX_BpBankAccounts_Primary");
        b.HasIndex(x => new { x.BusinessPartnerId, x.BankName, x.AccountNumber }).IsUnique();   // unique per bank within the partner
    }
}

internal sealed class BpRoleLogMap : IEntityTypeConfiguration<BpRoleLog>
{
    public void Configure(EntityTypeBuilder<BpRoleLog> b)
    {
        b.ToTable("BpRoleLog");
        b.HasKey(x => x.BpRoleLogId);
        b.Property(x => x.RoleCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.Action).HasMaxLength(10).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.UserName).HasMaxLength(200);
        b.HasIndex(x => new { x.BusinessPartnerId, x.OccurredOn });
    }
}

internal sealed class BpStatusLogMap : IEntityTypeConfiguration<BpStatusLog>
{
    public void Configure(EntityTypeBuilder<BpStatusLog> b)
    {
        b.ToTable("BpStatusLog");
        b.HasKey(x => x.BpStatusLogId);
        b.Property(x => x.FromStatus).HasMaxLength(12).IsRequired();
        b.Property(x => x.ToStatus).HasMaxLength(12).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.UserName).HasMaxLength(200);
        b.HasIndex(x => new { x.BusinessPartnerId, x.OccurredOn });
    }
}
