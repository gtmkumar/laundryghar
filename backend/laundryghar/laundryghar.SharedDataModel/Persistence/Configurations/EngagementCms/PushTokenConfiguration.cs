using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class PushTokenConfiguration : IEntityTypeConfiguration<PushToken>
{
    public void Configure(EntityTypeBuilder<PushToken> b)
    {
        b.ToTable("push_tokens", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.UserType).HasColumnName("user_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(10).IsRequired();
        b.Property(e => e.Token).HasColumnName("token").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(e => e.Token)
            .IsUnique()
            .HasDatabaseName("push_tokens_token_key");

        b.HasIndex(e => new { e.BrandId, e.CustomerId })
            .HasDatabaseName("idx_push_tokens_customer");

        b.HasIndex(e => new { e.BrandId, e.UserId })
            .HasDatabaseName("idx_push_tokens_user");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("push_tokens_brand_id_fkey");
    }
}
