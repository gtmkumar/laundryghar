using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys", "identity_access");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(e => e.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(32).IsRequired();
        b.Property(e => e.SecretHash).HasColumnName("secret_hash").IsRequired();
        b.Property(e => e.Environment).HasColumnName("environment").HasMaxLength(8).IsRequired();
        b.Property(e => e.Scopes).HasColumnName("scopes");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(e => e.RateLimitPerMinute).HasColumnName("rate_limit_per_minute");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        b.Property(e => e.RevokedByUserId).HasColumnName("revoked_by_user_id");
        b.Property(e => e.CreatedAt).HasColumnName("created_at");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.KeyPrefix).IsUnique().HasDatabaseName("api_keys_key_prefix_key");
    }
}

public class ApiKeyUsageConfiguration : IEntityTypeConfiguration<ApiKeyUsage>
{
    public void Configure(EntityTypeBuilder<ApiKeyUsage> b)
    {
        b.ToTable("api_key_usage", "identity_access");
        b.HasKey(e => new { e.ApiKeyId, e.UsageDate });

        b.Property(e => e.ApiKeyId).HasColumnName("api_key_id");
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.UsageDate).HasColumnName("usage_date");
        b.Property(e => e.RequestCount).HasColumnName("request_count");
        b.Property(e => e.ErrorCount).HasColumnName("error_count");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
    }
}
