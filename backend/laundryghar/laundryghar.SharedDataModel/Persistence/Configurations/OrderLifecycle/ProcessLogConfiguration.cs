using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class ProcessLogConfiguration : IEntityTypeConfiguration<ProcessLog>
{
    public void Configure(EntityTypeBuilder<ProcessLog> b)
    {
        b.ToTable("process_logs", "laundry_fulfillment");

        // Composite PK required by PG range partitioning on occurred_at
        b.HasKey(e => new { e.Id, e.OccurredAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        b.Property(e => e.BatchId).HasColumnName("batch_id");
        b.Property(e => e.GarmentId).HasColumnName("garment_id").IsRequired();
        b.Property(e => e.TagCode).HasColumnName("tag_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.ProcessId).HasColumnName("process_id");
        b.Property(e => e.ProcessCode).HasColumnName("process_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
        b.Property(e => e.FromStage).HasColumnName("from_stage").HasMaxLength(30);
        b.Property(e => e.ToStage).HasColumnName("to_stage").HasMaxLength(30);
        b.Property(e => e.PerformedByUserId).HasColumnName("performed_by_user_id");
        b.Property(e => e.PerformedByName).HasColumnName("performed_by_name").HasMaxLength(200);
        b.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("process_logs_brand_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("process_logs_warehouse_id_fkey");
        b.HasOne(e => e.Batch).WithMany().HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("process_logs_batch_id_fkey");
        b.HasOne(e => e.Garment).WithMany().HasForeignKey(e => e.GarmentId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("process_logs_garment_id_fkey");
        b.HasOne(e => e.Process).WithMany().HasForeignKey(e => e.ProcessId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("process_logs_process_id_fkey");
    }
}
