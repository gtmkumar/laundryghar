using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

/// <summary>Append-only usage ledger — no updates, no soft-delete.</summary>
public sealed class SubscriptionUsageLedgerConfiguration : IEntityTypeConfiguration<SubscriptionUsageLedger>
{
    public void Configure(EntityTypeBuilder<SubscriptionUsageLedger> b)
    {
        b.ToTable("subscription_usage_ledger", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerSubscriptionId).HasColumnName("customer_subscription_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start").IsRequired();
        b.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end").IsRequired();
        b.Property(e => e.TransactionType).HasColumnName("transaction_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceBefore).HasColumnName("balance_before").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.PerformedByType).HasColumnName("performed_by_type").HasMaxLength(20);
        b.Property(e => e.PerformedById).HasColumnName("performed_by_id");
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.HasOne(e => e.CustomerSubscription)
            .WithMany(cs => cs.UsageLedger)
            .HasForeignKey(e => e.CustomerSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("subscription_usage_ledger_customer_subscription_id_fkey");
    }
}
