using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Core.Domain;

namespace VMS.Modules.Core.Data.Maps;

internal sealed class LookupValueMap : IEntityTypeConfiguration<LookupValue>
{
    public void Configure(EntityTypeBuilder<LookupValue> b)
    {
        b.ToTable("LookupValues");
        b.HasKey(x => x.LookupValueID);
        b.Property(x => x.LookupType).HasMaxLength(40).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.Attributes).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.TenantId, x.LookupType, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.LookupType, x.SortOrder });
    }
}
