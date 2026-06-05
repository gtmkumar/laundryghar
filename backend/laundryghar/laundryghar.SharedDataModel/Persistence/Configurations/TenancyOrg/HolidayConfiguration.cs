using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("holidays", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ScopeId).HasColumnName("scope_id");
        b.Property(e => e.HolidayDate).HasColumnName("holiday_date").IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsFullDay).HasColumnName("is_full_day").IsRequired();
        b.Property(e => e.PartialOpenFrom).HasColumnName("partial_open_from").HasColumnType("time without time zone");
        b.Property(e => e.PartialOpenTo).HasColumnName("partial_open_to").HasColumnType("time without time zone");
        b.Property(e => e.AcceptsOrders).HasColumnName("accepts_orders").IsRequired();
        b.Property(e => e.IsRecurring).HasColumnName("is_recurring").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
    }
}
