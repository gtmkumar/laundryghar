using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

public sealed class CustomerSubscriptionConfiguration : IEntityTypeConfiguration<CustomerSubscription>
{
    public void Configure(EntityTypeBuilder<CustomerSubscription> b)
    {
        b.ToTable("customer_subscriptions", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.PlanId).HasColumnName("plan_id").IsRequired();
        b.Property(e => e.MandateId).HasColumnName("mandate_id");
        b.Property(e => e.SubscriptionNumber).HasColumnName("subscription_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.PriceSnapshot).HasColumnName("price_snapshot").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20).IsRequired();
        b.Property(e => e.IntervalCount).HasColumnName("interval_count").IsRequired();
        b.Property(e => e.QuotaType).HasColumnName("quota_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.QuotaValue).HasColumnName("quota_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AutoRenew).HasColumnName("auto_renew").IsRequired();
        b.Property(e => e.CurrentPeriodStart).HasColumnName("current_period_start");
        b.Property(e => e.CurrentPeriodEnd).HasColumnName("current_period_end");
        b.Property(e => e.NextBillingAt).HasColumnName("next_billing_at");
        b.Property(e => e.TrialEndsAt).HasColumnName("trial_ends_at");
        b.Property(e => e.CreditsRemaining).HasColumnName("credits_remaining").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.StartedAt).HasColumnName("started_at");
        b.Property(e => e.ActivatedAt).HasColumnName("activated_at");
        b.Property(e => e.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").IsRequired();
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancelReason).HasColumnName("cancel_reason");
        b.Property(e => e.PausedAt).HasColumnName("paused_at");
        b.Property(e => e.PauseResumesAt).HasColumnName("pause_resumes_at");
        b.Property(e => e.EndedAt).HasColumnName("ended_at");
        b.Property(e => e.PastDueSince).HasColumnName("past_due_since");
        b.Property(e => e.DunningAttempts).HasColumnName("dunning_attempts").IsRequired();
        b.Property(e => e.FailedPaymentCount).HasColumnName("failed_payment_count").IsRequired();
        b.Property(e => e.TotalCyclesBilled).HasColumnName("total_cycles_billed").IsRequired();
        b.Property(e => e.GatewaySubscriptionId).HasColumnName("gateway_subscription_id").HasMaxLength(100);
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.Version).HasColumnName("version").IsRequired();

        b.HasIndex(e => new { e.BrandId, e.SubscriptionNumber })
            .IsUnique()
            .HasDatabaseName("customer_subscriptions_brand_id_subscription_number_key");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customer_subscriptions_customer_id_fkey");

        b.HasOne(e => e.Plan)
            .WithMany(p => p.CustomerSubscriptions)
            .HasForeignKey(e => e.PlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customer_subscriptions_plan_id_fkey");

        b.HasOne(e => e.Mandate)
            .WithMany(m => m.CustomerSubscriptions)
            .HasForeignKey(e => e.MandateId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("customer_subscriptions_mandate_id_fkey");
    }
}
