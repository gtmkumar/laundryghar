using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class WarehouseBatchConfiguration : IEntityTypeConfiguration<WarehouseBatch>
{
    public void Configure(EntityTypeBuilder<WarehouseBatch> b)
    {
        b.ToTable("warehouse_batches", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        b.Property(e => e.BatchNumber).HasColumnName("batch_number").HasMaxLength(50).IsRequired();
        b.Property(e => e.BatchType).HasColumnName("batch_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.ServiceId).HasColumnName("service_id");
        b.Property(e => e.MachineId).HasColumnName("machine_id").HasMaxLength(50);
        b.Property(e => e.CycleProgram).HasColumnName("cycle_program").HasMaxLength(50);
        b.Property(e => e.ExpectedGarmentCount).HasColumnName("expected_garment_count").IsRequired();
        b.Property(e => e.ActualGarmentCount).HasColumnName("actual_garment_count").IsRequired();
        b.Property(e => e.TotalWeightGrams).HasColumnName("total_weight_grams");
        b.Property(e => e.StartedAt).HasColumnName("started_at");
        b.Property(e => e.StartedBy).HasColumnName("started_by");
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.CompletedBy).HasColumnName("completed_by");
        b.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
        b.Property(e => e.ChemicalsUsed).HasColumnName("chemicals_used").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.TemperatureCelsius).HasColumnName("temperature_celsius").HasColumnType("numeric(5,2)");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.FailureReason).HasColumnName("failure_reason");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.BatchNumber).IsUnique().HasDatabaseName("warehouse_batches_batch_number_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("warehouse_batches_brand_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("warehouse_batches_warehouse_id_fkey");
        b.HasOne(e => e.Service).WithMany().HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("warehouse_batches_service_id_fkey");
    }
}
