using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderSettlementConfiguration : IEntityTypeConfiguration<RiderSettlement>
{
    public void Configure(EntityTypeBuilder<RiderSettlement> b)
    {
        b.ToTable("rider_settlements", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(e => e.CollectionCount).HasColumnName("collection_count").IsRequired();
        b.Property(e => e.Reference).HasColumnName("reference");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.SettledAt).HasColumnName("settled_at").IsRequired();
        b.Property(e => e.SettledBy).HasColumnName("settled_by");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
    }
}
