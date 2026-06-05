using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class WarehouseProcessConfiguration : IEntityTypeConfiguration<WarehouseProcess>
{
    public void Configure(EntityTypeBuilder<WarehouseProcess> b)
    {
        b.ToTable("warehouse_processes", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.ProcessCategory).HasColumnName("process_category").HasMaxLength(30).IsRequired();
        b.Property(e => e.SequenceOrder).HasColumnName("sequence_order").IsRequired();
        b.Property(e => e.ExpectedDurationMin).HasColumnName("expected_duration_min");
        b.Property(e => e.RequiresMachine).HasColumnName("requires_machine").IsRequired();
        b.Property(e => e.RequiresSupervisor).HasColumnName("requires_supervisor").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("warehouse_processes_brand_id_code_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("warehouse_processes_brand_id_fkey");
    }
}
