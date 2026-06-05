using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class QualityCheckConfiguration : IEntityTypeConfiguration<QualityCheck>
{
    public void Configure(EntityTypeBuilder<QualityCheck> b)
    {
        b.ToTable("quality_checks", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        b.Property(e => e.GarmentId).HasColumnName("garment_id").IsRequired();
        // Scalar-only composite FK columns to orders
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.BatchId).HasColumnName("batch_id");
        b.Property(e => e.QcRound).HasColumnName("qc_round").IsRequired();
        b.Property(e => e.InspectorUserId).HasColumnName("inspector_user_id").IsRequired();
        b.Property(e => e.InspectedAt).HasColumnName("inspected_at").IsRequired();
        b.Property(e => e.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        b.Property(e => e.Issues).HasColumnName("issues").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.PreWashInspectionId).HasColumnName("pre_wash_inspection_id");
        b.Property(e => e.PostWashInspectionId).HasColumnName("post_wash_inspection_id");
        b.Property(e => e.ComparisonNotes).HasColumnName("comparison_notes");
        b.Property(e => e.RequiresRewash).HasColumnName("requires_rewash").IsRequired();
        b.Property(e => e.RewashPriority).HasColumnName("rewash_priority").HasMaxLength(20);
        b.Property(e => e.SupervisorApproval).HasColumnName("supervisor_approval").IsRequired();
        b.Property(e => e.SupervisorUserId).HasColumnName("supervisor_user_id");
        b.Property(e => e.SupervisorApprovedAt).HasColumnName("supervisor_approved_at");
        b.Property(e => e.CustomerCommunicated).HasColumnName("customer_communicated").IsRequired();
        b.Property(e => e.CustomerCommunicatedAt).HasColumnName("customer_communicated_at");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("quality_checks_brand_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("quality_checks_warehouse_id_fkey");
        b.HasOne(e => e.Garment).WithMany(g => g.QualityChecks)
            .HasForeignKey(e => e.GarmentId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("quality_checks_garment_id_fkey");
        b.HasOne(e => e.Batch).WithMany(wb => wb.QualityChecks)
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("quality_checks_batch_id_fkey");
        b.HasOne(e => e.PreWashInspection).WithMany().HasForeignKey(e => e.PreWashInspectionId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("quality_checks_pre_wash_inspection_id_fkey");
        b.HasOne(e => e.PostWashInspection).WithMany().HasForeignKey(e => e.PostWashInspectionId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("quality_checks_post_wash_inspection_id_fkey");
    }
}
