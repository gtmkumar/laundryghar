using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class OperatingHourConfiguration : IEntityTypeConfiguration<OperatingHour>
{
    public void Configure(EntityTypeBuilder<OperatingHour> b)
    {
        b.ToTable("operating_hours", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ScopeId).HasColumnName("scope_id").IsRequired();
        b.Property(e => e.DayOfWeek).HasColumnName("day_of_week").IsRequired();
        b.Property(e => e.IsClosed).HasColumnName("is_closed").IsRequired();
        b.Property(e => e.OpenTime).HasColumnName("open_time").HasColumnType("time without time zone");
        b.Property(e => e.CloseTime).HasColumnName("close_time").HasColumnType("time without time zone");
        b.Property(e => e.BreakStart).HasColumnName("break_start").HasColumnType("time without time zone");
        b.Property(e => e.BreakEnd).HasColumnName("break_end").HasColumnType("time without time zone");
        b.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(255);
        b.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.ScopeType, e.ScopeId, e.DayOfWeek, e.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("operating_hours_scope_type_scope_id_day_of_week_effective_f_key");
    }
}
