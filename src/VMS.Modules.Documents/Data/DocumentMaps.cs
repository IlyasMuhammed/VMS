using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Documents.Domain;

namespace VMS.Modules.Documents.Data;

internal sealed class DocumentTypeMap : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> b)
    {
        b.ToTable("DocumentTypes");
        b.HasKey(x => x.DocumentTypeId);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.PartnerRole).HasMaxLength(30);
        b.Property(x => x.DefaultValidityUnit).HasMaxLength(10);
        b.Property(x => x.MandatoryLevel).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.AppliesTo });
    }
}

internal sealed class DocumentMap : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("Documents");
        b.HasKey(x => x.DocumentId);
        b.Property(x => x.DocumentCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.OwnerType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.Property(x => x.DocumentNumber).HasMaxLength(60);
        b.Property(x => x.Provider).HasMaxLength(120);
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.RejectReason).HasMaxLength(500);

        // BR-DOC-001: one current version per owner and type.
        b.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.DocumentTypeId }).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_Documents_Current");
        b.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId, x.DocumentTypeId, x.VersionNo }).IsUnique().HasDatabaseName("UX_Documents_Version");
        b.HasIndex(x => new { x.TenantId, x.Status, x.ExpiryDate });
        b.HasIndex(x => new { x.TenantId, x.DocumentTypeId });
    }
}
