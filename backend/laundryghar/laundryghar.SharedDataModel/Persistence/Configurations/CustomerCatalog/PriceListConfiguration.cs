using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> b)
    {
        b.ToTable("price_lists", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.VersionNumber).HasColumnName("version_number").IsRequired();
        b.Property(e => e.ParentPriceListId).HasColumnName("parent_price_list_id");
        b.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        b.Property(e => e.IsDefault).HasColumnName("is_default").IsRequired();
        b.Property(e => e.IsPublished).HasColumnName("is_published").IsRequired();
        b.Property(e => e.PublishedAt).HasColumnName("published_at");
        b.Property(e => e.PublishedBy).HasColumnName("published_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code, e.VersionNumber })
            .IsUnique()
            .HasDatabaseName("price_lists_brand_id_code_version_number_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("price_lists_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("price_lists_franchise_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("price_lists_store_id_fkey");

        // Self-referential: parent price list
        b.HasOne(e => e.ParentPriceList)
            .WithMany()
            .HasForeignKey(e => e.ParentPriceListId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("price_lists_parent_price_list_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
