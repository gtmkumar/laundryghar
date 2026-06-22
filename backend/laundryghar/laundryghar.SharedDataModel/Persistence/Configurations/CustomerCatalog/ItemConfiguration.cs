using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ItemGroupId).HasColumnName("item_group_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.ImageUrl).HasColumnName("image_url");
        b.Property(e => e.TypicalWeightGrams).HasColumnName("typical_weight_grams");
        b.Property(e => e.RequiresPerSidePrice).HasColumnName("requires_per_side_price").IsRequired();
        b.Property(e => e.TatHours).HasColumnName("tat_hours");
        b.Property(e => e.ExpressEligible).HasColumnName("express_eligible").IsRequired();
        b.Property(e => e.ExpressSurcharge).HasColumnName("express_surcharge").HasColumnType("numeric(10,2)");
        // search_tokens is a DB-managed tsvector column (updated by trigger).
        // Npgsql EF 10 does not support mapping string → tsvector directly.
        // Ignoring it from the EF model is safe: we never write it and it is not needed in queries.
        // If full-text search is required in the future, use ExecuteSqlRaw with to_tsquery/plainto_tsquery.
        b.Ignore(e => e.SearchTokens);
        b.Property(e => e.Aliases).HasColumnName("aliases").HasColumnType("text[]").IsRequired();
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("items_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("items_brand_id_fkey");

        b.HasOne(e => e.ItemGroup)
            .WithMany(g => g.Items)
            .HasForeignKey(e => e.ItemGroupId)
            .OnDelete(DeleteBehavior.NoAction)    // DB: no explicit ON DELETE
            .HasConstraintName("items_item_group_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
