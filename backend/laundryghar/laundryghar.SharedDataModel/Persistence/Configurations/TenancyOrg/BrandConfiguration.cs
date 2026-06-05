using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.ToTable("brands", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.PlatformId).HasColumnName("platform_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.LegalName).HasColumnName("legal_name").HasMaxLength(200);
        b.Property(e => e.Tagline).HasColumnName("tagline").HasMaxLength(300);
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.LogoUrl).HasColumnName("logo_url");
        b.Property(e => e.FaviconUrl).HasColumnName("favicon_url");
        b.Property(e => e.PrimaryColor).HasColumnName("primary_color").HasColumnType("character(7)");
        b.Property(e => e.SecondaryColor).HasColumnName("secondary_color").HasColumnType("character(7)");
        b.Property(e => e.AccentColor).HasColumnName("accent_color").HasColumnType("character(7)");
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)").IsRequired();
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        b.Property(e => e.LocaleDefault).HasColumnName("locale_default").HasMaxLength(10).IsRequired();
        b.Property(e => e.LocalesEnabled).HasColumnName("locales_enabled").HasColumnType("text[]").IsRequired();
        b.Property(e => e.SupportEmail).HasColumnName("support_email").HasColumnType("citext");
        b.Property(e => e.SupportPhone).HasColumnName("support_phone").HasMaxLength(20);
        b.Property(e => e.TollFreeNumber).HasColumnName("toll_free_number").HasMaxLength(20);
        b.Property(e => e.WhatsappNumber).HasColumnName("whatsapp_number").HasMaxLength(20);
        b.Property(e => e.WebsiteUrl).HasColumnName("website_url");
        b.Property(e => e.PrivacyPolicyUrl).HasColumnName("privacy_policy_url");
        b.Property(e => e.TermsUrl).HasColumnName("terms_url");
        b.Property(e => e.PlayStoreUrl).HasColumnName("play_store_url");
        b.Property(e => e.AppStoreUrl).HasColumnName("app_store_url");
        b.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.LaunchedAt).HasColumnName("launched_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => e.Code).IsUnique().HasDatabaseName("brands_code_key");

        b.HasOne(e => e.Platform)
            .WithMany(p => p.Brands)
            .HasForeignKey(e => e.PlatformId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("brands_platform_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
