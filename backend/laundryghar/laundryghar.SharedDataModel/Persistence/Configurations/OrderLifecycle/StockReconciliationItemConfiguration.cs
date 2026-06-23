using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class StockReconciliationItemConfiguration : IEntityTypeConfiguration<StockReconciliationItem>
{
    public void Configure(EntityTypeBuilder<StockReconciliationItem> b)
    {
        b.ToTable("stock_reconciliation_items", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.ReconciliationId).HasColumnName("reconciliation_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.GarmentId).HasColumnName("garment_id");
        b.Property(e => e.TagCode).HasColumnName("tag_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.ExpectedStage).HasColumnName("expected_stage").HasMaxLength(30);
        b.Property(e => e.ExpectedLocationType).HasColumnName("expected_location_type").HasMaxLength(20);
        b.Property(e => e.ExpectedLocationId).HasColumnName("expected_location_id");
        b.Property(e => e.FoundStage).HasColumnName("found_stage").HasMaxLength(30);
        b.Property(e => e.FoundLocationType).HasColumnName("found_location_type").HasMaxLength(20);
        b.Property(e => e.FoundLocationId).HasColumnName("found_location_id");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.LastKnownHolderType).HasColumnName("last_known_holder_type").HasMaxLength(20);
        b.Property(e => e.LastKnownHolderId).HasColumnName("last_known_holder_id");
        b.Property(e => e.LastScannedAt).HasColumnName("last_scanned_at");
        b.Property(e => e.ResolutionAction).HasColumnName("resolution_action").HasMaxLength(30);
        b.Property(e => e.ResolutionNotes).HasColumnName("resolution_notes");
        b.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        b.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
        b.Property(e => e.FlaggedAt).HasColumnName("flagged_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Reconciliation).WithMany(r => r.Items)
            .HasForeignKey(e => e.ReconciliationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("stock_reconciliation_items_reconciliation_id_fkey");
        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("stock_reconciliation_items_brand_id_fkey");
        b.HasOne(e => e.Garment).WithMany().HasForeignKey(e => e.GarmentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("stock_reconciliation_items_garment_id_fkey");
    }
}
