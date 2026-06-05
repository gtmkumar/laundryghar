using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerCode).HasColumnName("customer_code").HasMaxLength(30).IsRequired();
        b.Property(e => e.PhoneE164).HasColumnName("phone_e164").HasMaxLength(20).IsRequired();
        b.Property(e => e.Email).HasColumnName("email").HasColumnType("citext");
        b.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
        b.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
        b.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        b.Property(e => e.Gender).HasColumnName("gender").HasMaxLength(20);
        b.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
        b.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
        b.Property(e => e.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        b.Property(e => e.PrimaryStoreId).HasColumnName("primary_store_id");
        b.Property(e => e.ReferralCode).HasColumnName("referral_code").HasMaxLength(20);
        b.Property(e => e.ReferredByCustomerId).HasColumnName("referred_by_customer_id");
        b.Property(e => e.LifetimeOrders).HasColumnName("lifetime_orders").IsRequired();
        b.Property(e => e.LifetimeSpend).HasColumnName("lifetime_spend").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AvgOrderValue).HasColumnName("avg_order_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.LastOrderAt).HasColumnName("last_order_at");
        b.Property(e => e.FirstOrderAt).HasColumnName("first_order_at");
        b.Property(e => e.LoyaltyPointsBalance).HasColumnName("loyalty_points_balance").IsRequired();
        b.Property(e => e.WalletBalance).HasColumnName("wallet_balance").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CustomerSegment).HasColumnName("customer_segment").HasMaxLength(30);
        b.Property(e => e.RiskFlag).HasColumnName("risk_flag").HasMaxLength(20);
        b.Property(e => e.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        b.Property(e => e.PhoneVerifiedAt).HasColumnName("phone_verified_at");
        b.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
        b.Property(e => e.OnboardingCompletedAt).HasColumnName("onboarding_completed_at");
        b.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
        b.Property(e => e.MarketingOptIn).HasColumnName("marketing_opt_in").IsRequired();
        b.Property(e => e.SmsOptIn).HasColumnName("sms_opt_in").IsRequired();
        b.Property(e => e.WhatsappOptIn).HasColumnName("whatsapp_opt_in").IsRequired();
        b.Property(e => e.EmailOptIn).HasColumnName("email_opt_in").IsRequired();
        b.Property(e => e.PushOptIn).HasColumnName("push_opt_in").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.CustomerCode }).IsUnique().HasDatabaseName("customers_brand_id_customer_code_key");
        b.HasIndex(e => new { e.BrandId, e.PhoneE164 }).IsUnique().HasDatabaseName("customers_brand_id_phone_e164_key");
        b.HasIndex(e => e.ReferralCode).IsUnique().HasDatabaseName("customers_referral_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customers_brand_id_fkey");

        b.HasOne(e => e.PrimaryStore)
            .WithMany()
            .HasForeignKey(e => e.PrimaryStoreId)
            .OnDelete(DeleteBehavior.NoAction)    // DB: no explicit ON DELETE → default restrict
            .HasConstraintName("customers_primary_store_id_fkey");

        // Self-referential FK — no explicit ON DELETE in DB
        b.HasOne(e => e.ReferredByCustomer)
            .WithMany()
            .HasForeignKey(e => e.ReferredByCustomerId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("customers_referred_by_customer_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
