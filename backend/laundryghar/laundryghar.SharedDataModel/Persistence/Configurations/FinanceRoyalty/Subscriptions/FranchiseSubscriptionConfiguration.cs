using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty.Subscriptions;

public sealed class FranchiseSubscriptionConfiguration : IEntityTypeConfiguration<FranchiseSubscription>
{
    public void Configure(EntityTypeBuilder<FranchiseSubscription> b)
    {
        b.ToTable("franchise_subscriptions", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.PlatformPlanId).HasColumnName("platform_plan_id").IsRequired();
        b.Property(e => e.SubscriptionNumber).HasColumnName("subscription_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.PriceSnapshot).HasColumnName("price_snapshot").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20).IsRequired();
        b.Property(e => e.IntervalCount).HasColumnName("interval_count").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.MaxStores).HasColumnName("max_stores");
        b.Property(e => e.MaxWarehouses).HasColumnName("max_warehouses");
        b.Property(e => e.MaxUsers).HasColumnName("max_users");
        b.Property(e => e.MaxOrdersPerMonth).HasColumnName("max_orders_per_month");
        b.Property(e => e.MaxRiders).HasColumnName("max_riders");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AutoRenew).HasColumnName("auto_renew").IsRequired();
        b.Property(e => e.PaymentMethod).HasColumnName("payment_method").HasMaxLength(20).IsRequired();
        b.Property(e => e.GatewayMandateId).HasColumnName("gateway_mandate_id").HasMaxLength(100);
        b.Property(e => e.GatewaySubscriptionId).HasColumnName("gateway_subscription_id").HasMaxLength(100);
        b.Property(e => e.CurrentPeriodStart).HasColumnName("current_period_start");
        b.Property(e => e.CurrentPeriodEnd).HasColumnName("current_period_end");
        b.Property(e => e.NextBillingAt).HasColumnName("next_billing_at");
        b.Property(e => e.TrialEndsAt).HasColumnName("trial_ends_at");
        b.Property(e => e.CurrentPeriodOrders).HasColumnName("current_period_orders").IsRequired();
        b.Property(e => e.StartedAt).HasColumnName("started_at");
        b.Property(e => e.ActivatedAt).HasColumnName("activated_at");
        b.Property(e => e.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").IsRequired();
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancelReason).HasColumnName("cancel_reason");
        b.Property(e => e.PastDueSince).HasColumnName("past_due_since");
        b.Property(e => e.DunningAttempts).HasColumnName("dunning_attempts").IsRequired();
        b.Property(e => e.SuspendGraceUntil).HasColumnName("suspend_grace_until");
        b.Property(e => e.SuspendedAt).HasColumnName("suspended_at");
        b.Property(e => e.SuspendedReason).HasColumnName("suspended_reason");
        b.Property(e => e.ReactivatedAt).HasColumnName("reactivated_at");
        b.Property(e => e.EndedAt).HasColumnName("ended_at");
        b.Property(e => e.TotalCyclesBilled).HasColumnName("total_cycles_billed").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.Version).HasColumnName("version").IsRequired();

        b.HasIndex(e => new { e.BrandId, e.SubscriptionNumber })
            .IsUnique()
            .HasDatabaseName("franchise_subscriptions_brand_id_subscription_number_key");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("franchise_subscriptions_franchise_id_fkey");

        b.HasOne(e => e.PlatformPlan)
            .WithMany(p => p.FranchiseSubscriptions)
            .HasForeignKey(e => e.PlatformPlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("franchise_subscriptions_platform_plan_id_fkey");
    }
}
