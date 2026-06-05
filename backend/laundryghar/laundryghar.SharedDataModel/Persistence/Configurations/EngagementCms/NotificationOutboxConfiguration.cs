using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> b)
    {
        b.ToTable("notifications_outbox", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.TemplateId).HasColumnName("template_id");
        b.Property(e => e.TemplateCode).HasColumnName("template_code").HasMaxLength(100).IsRequired();
        b.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).IsRequired();
        b.Property(e => e.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
        b.Property(e => e.RecipientType).HasColumnName("recipient_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.RecipientId).HasColumnName("recipient_id");
        b.Property(e => e.RecipientPhone).HasColumnName("recipient_phone").HasMaxLength(20);
        b.Property(e => e.RecipientEmail).HasColumnName("recipient_email").HasColumnType("citext");
        b.Property(e => e.RecipientFcmToken).HasColumnName("recipient_fcm_token");
        b.Property(e => e.RecipientApnsToken).HasColumnName("recipient_apns_token");
        b.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(500);
        b.Property(e => e.Body).HasColumnName("body").IsRequired();
        b.Property(e => e.VariablesResolved).HasColumnName("variables_resolved").HasColumnType("jsonb");
        b.Property(e => e.PushTitle).HasColumnName("push_title").HasMaxLength(200);
        b.Property(e => e.PushDeeplink).HasColumnName("push_deeplink");
        b.Property(e => e.PushPayload).HasColumnName("push_payload").HasColumnType("jsonb");
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        b.Property(e => e.Priority).HasColumnName("priority").IsRequired();
        b.Property(e => e.ScheduledAt).HasColumnName("scheduled_at").IsRequired();
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(e => e.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        b.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(e => e.LastAttemptAt).HasColumnName("last_attempt_at");
        b.Property(e => e.LastError).HasColumnName("last_error");
        b.Property(e => e.SentAt).HasColumnName("sent_at");
        b.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(50);
        b.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(200);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.SuppressionReason).HasColumnName("suppression_reason").HasMaxLength(100);
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("notifications_outbox_idempotency_key_key");

        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_notifications_outbox_brand_id_fk");
        b.HasIndex(e => new { e.ScheduledAt, e.Priority }).HasDatabaseName("idx_outbox_due");
        b.HasIndex(e => new { e.RecipientType, e.RecipientId, e.CreatedAt }).HasDatabaseName("idx_outbox_recipient");
        b.HasIndex(e => new { e.ReferenceType, e.ReferenceId }).HasDatabaseName("idx_outbox_reference");
        b.HasIndex(e => new { e.NextAttemptAt, e.Priority }).HasDatabaseName("idx_outbox_retry");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("notifications_outbox_brand_id_fkey");

        b.HasOne(e => e.Template)
            .WithMany(t => t.NotificationOutboxes)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("notifications_outbox_template_id_fkey");
    }
}
