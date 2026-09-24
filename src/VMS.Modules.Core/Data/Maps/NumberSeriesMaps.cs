using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Core.Domain;

namespace VMS.Modules.Core.Data.Maps;

internal sealed class NumberSeriesMap : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> b)
    {
        b.ToTable("NumberSeries");
        b.HasKey(x => x.NumberSeriesID);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
        b.Property(x => x.ResetPeriod).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

internal sealed class NumberSeriesCounterMap : IEntityTypeConfiguration<NumberSeriesCounter>
{
    public void Configure(EntityTypeBuilder<NumberSeriesCounter> b)
    {
        b.ToTable("NumberSeriesCounters");
        b.HasKey(x => new { x.NumberSeriesID, x.PeriodKey });
        b.Property(x => x.PeriodKey).HasMaxLength(8).IsRequired();
        b.HasOne<NumberSeries>().WithMany().HasForeignKey(x => x.NumberSeriesID).OnDelete(DeleteBehavior.Cascade);
    }
}
