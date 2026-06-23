using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class GarmentTagConfiguration : IEntityTypeConfiguration<GarmentTag>
{
    public void Configure(EntityTypeBuilder<GarmentTag> b)
    {
        b.ToTable("garment_tags", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.TagCode).HasColumnName("tag_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.TagFormat).HasColumnName("tag_format").HasMaxLength(20).IsRequired();
        b.Property(e => e.BatchNumber).HasColumnName("batch_number").HasMaxLength(50);
        b.Property(e => e.PrintedAt).HasColumnName("printed_at");
        b.Property(e => e.PrintedBy).HasColumnName("printed_by");
        b.Property(e => e.PrinterId).HasColumnName("printer_id").HasMaxLength(100);
        b.Property(e => e.AssignedToGarmentId).HasColumnName("assigned_to_garment_id");
        b.Property(e => e.AssignedAt).HasColumnName("assigned_at");
        b.Property(e => e.AssignedBy).HasColumnName("assigned_by");
        b.Property(e => e.IsDamaged).HasColumnName("is_damaged").IsRequired();
        b.Property(e => e.IsReprinted).HasColumnName("is_reprinted").IsRequired();
        b.Property(e => e.ReprintCount).HasColumnName("reprint_count").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.TagCode).IsUnique().HasDatabaseName("garment_tags_tag_code_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garment_tags_brand_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garment_tags_store_id_fkey");
        b.HasOne(e => e.AssignedToGarment).WithMany().HasForeignKey(e => e.AssignedToGarmentId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("garment_tags_assigned_to_garment_id_fkey");
    }
}
