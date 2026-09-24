using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Core.Domain;

namespace VMS.Modules.Core.Data.Maps;

internal sealed class BranchMap : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches");
        b.HasKey(x => x.BranchId);
        b.Property(x => x.BranchId).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
