using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

/// <summary>Append-only — no updates. No global query filter (no deleted_at column).</summary>
public sealed class SubscriptionBillingAttemptConfiguration : IEntityTypeConfiguration<SubscriptionBillingAttempt>
{
    public void Configure(EntityTypeBuilder<SubscriptionBillingAttempt> b)
    {
        b.ToTable("subscription_billing_attempts", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerSubscriptionId).HasColumnName("customer_subscription_id").IsRequired();
        b.Property(e => e.SubscriptionInvoiceId).HasColumnName("subscription_invoice_id").IsRequired();
        b.Property(e => e.MandateId).HasColumnName("mandate_id");
        b.Property(e => e.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30);
        b.Property(e => e.GatewayPaymentId).HasColumnName("gateway_payment_id").HasMaxLength(100);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.FailureCode).HasColumnName("failure_code").HasMaxLength(50);
        b.Property(e => e.FailureMessage).HasColumnName("failure_message");
        b.Property(e => e.GatewayResponse).HasColumnName("gateway_response").HasColumnType("jsonb");
        b.Property(e => e.AttemptedAt).HasColumnName("attempted_at").IsRequired();
        b.Property(e => e.NextRetryAt).HasColumnName("next_retry_at");
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(e => e.IdempotencyKey).IsUnique()
            .HasDatabaseName("subscription_billing_attempts_idempotency_key_key")
            .HasFilter("idempotency_key IS NOT NULL");

        b.HasOne(e => e.CustomerSubscription)
            .WithMany(cs => cs.BillingAttempts)
            .HasForeignKey(e => e.CustomerSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("subscription_billing_attempts_customer_subscription_id_fkey");

        b.HasOne(e => e.SubscriptionInvoice)
            .WithMany(i => i.BillingAttempts)
            .HasForeignKey(e => e.SubscriptionInvoiceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("subscription_billing_attempts_subscription_invoice_id_fkey");

        b.HasOne(e => e.Mandate)
            .WithMany()
            .HasForeignKey(e => e.MandateId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("subscription_billing_attempts_mandate_id_fkey");
    }
}
