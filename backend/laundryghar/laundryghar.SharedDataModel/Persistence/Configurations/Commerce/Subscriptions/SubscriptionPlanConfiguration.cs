using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.ToTable("subscription_plans", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.Tier).HasColumnName("tier").HasMaxLength(30).IsRequired();
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20).IsRequired();
        b.Property(e => e.IntervalCount).HasColumnName("interval_count").IsRequired();
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.SetupFee).HasColumnName("setup_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.TrialDays).HasColumnName("trial_days").IsRequired();
        b.Property(e => e.QuotaType).HasColumnName("quota_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.QuotaValue).HasColumnName("quota_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.RolloverUnused).HasColumnName("rollover_unused").IsRequired();
        b.Property(e => e.MaxRollover).HasColumnName("max_rollover").HasColumnType("numeric(14,2)");
        b.Property(e => e.OverageDiscountPercent).HasColumnName("overage_discount_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.ApplicableServices).HasColumnName("applicable_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.ExcludedServices).HasColumnName("excluded_services").HasColumnType("uuid[]").IsRequired();
        // Fulfilment-leg inclusions live in the fulfillment_inclusions jsonb (owned type, ToJson) —
        // demoted off the generic plan spine in multi-vertical Phase 2 (slice 2E).
        b.OwnsOne(e => e.Inclusions, a =>
        {
            a.ToJson("fulfillment_inclusions");
            a.Property(x => x.PickupIncluded).HasJsonPropertyName("pickup_included");
            a.Property(x => x.DeliveryIncluded).HasJsonPropertyName("delivery_included");
            a.Property(x => x.ExpressIncluded).HasJsonPropertyName("express_included");
        });
        b.Navigation(e => e.Inclusions).IsRequired();
        b.Property(e => e.MaxActiveSubscribers).HasColumnName("max_active_subscribers");
        b.Property(e => e.CurrentSubscriberCount).HasColumnName("current_subscriber_count").IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30);
        b.Property(e => e.GatewayPlanId).HasColumnName("gateway_plan_id").HasMaxLength(100);
        b.Property(e => e.TermsAndConditions).HasColumnName("terms_and_conditions");
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.ColorHex).HasColumnName("color_hex").HasColumnType("character(7)");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsPublic).HasColumnName("is_public").IsRequired();
        b.Property(e => e.IsFeatured).HasColumnName("is_featured").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AvailableFrom).HasColumnName("available_from");
        b.Property(e => e.AvailableTo).HasColumnName("available_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("subscription_plans_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("subscription_plans_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
