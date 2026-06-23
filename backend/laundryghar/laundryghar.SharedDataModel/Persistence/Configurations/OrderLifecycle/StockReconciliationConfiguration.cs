using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class StockReconciliationConfiguration : IEntityTypeConfiguration<StockReconciliation>
{
    public void Configure(EntityTypeBuilder<StockReconciliation> b)
    {
        b.ToTable("stock_reconciliations", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.ReconDate).HasColumnName("recon_date").IsRequired();
        b.Property(e => e.ReconType).HasColumnName("recon_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.StartedAt).HasColumnName("started_at").IsRequired();
        b.Property(e => e.StartedBy).HasColumnName("started_by").IsRequired();
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.CompletedBy).HasColumnName("completed_by");
        b.Property(e => e.ExpectedCount).HasColumnName("expected_count").IsRequired();
        b.Property(e => e.ScannedCount).HasColumnName("scanned_count").IsRequired();
        b.Property(e => e.MatchedCount).HasColumnName("matched_count").IsRequired();
        b.Property(e => e.MissingCount).HasColumnName("missing_count").IsRequired();
        b.Property(e => e.UnexpectedCount).HasColumnName("unexpected_count").IsRequired();
        b.Property(e => e.DamagedCount).HasColumnName("damaged_count").IsRequired();
        b.Property(e => e.ResolvedMissingCount).HasColumnName("resolved_missing_count").IsRequired();
        b.Property(e => e.Summary).HasColumnName("summary").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("stock_reconciliations_brand_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("stock_reconciliations_warehouse_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("stock_reconciliations_store_id_fkey");
    }
}
