using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class GarmentConditionConfiguration : IEntityTypeConfiguration<GarmentCondition>
{
    public void Configure(EntityTypeBuilder<GarmentCondition> b)
    {
        b.ToTable("garment_conditions", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        b.Property(e => e.SeverityLevels).HasColumnName("severity_levels").HasColumnType("text[]").IsRequired();
        b.Property(e => e.RequiresDisclaimer).HasColumnName("requires_disclaimer").IsRequired();
        b.Property(e => e.DisclaimerText).HasColumnName("disclaimer_text");
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("garment_conditions_brand_id_code_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garment_conditions_brand_id_fkey");
    }
}
