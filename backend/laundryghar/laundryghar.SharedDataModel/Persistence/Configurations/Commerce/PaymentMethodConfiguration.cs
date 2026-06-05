using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("payment_methods", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.MethodType).HasColumnName("method_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30);
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.MinimumAmount).HasColumnName("minimum_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.MaximumAmount).HasColumnName("maximum_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.ConvenienceFeeType).HasColumnName("convenience_fee_type").HasMaxLength(20);
        b.Property(e => e.ConvenienceFeeValue).HasColumnName("convenience_fee_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.IsOnline).HasColumnName("is_online").IsRequired();
        b.Property(e => e.IsRefundable).HasColumnName("is_refundable").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("payment_methods_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payment_methods_brand_id_fkey");
    }
}
