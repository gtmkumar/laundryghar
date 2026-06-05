using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        b.ToTable("notifications_log", "engagement_cms");

        // Composite PK required by PG range partitioning on sent_at
        b.HasKey(e => new { e.Id, e.SentAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.SentAt).HasColumnName("sent_at").IsRequired();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.OutboxId).HasColumnName("outbox_id");
        b.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).IsRequired();
        b.Property(e => e.TemplateCode).HasColumnName("template_code").HasMaxLength(100);
        b.Property(e => e.RecipientType).HasColumnName("recipient_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.RecipientId).HasColumnName("recipient_id");
        b.Property(e => e.RecipientAddress).HasColumnName("recipient_address").HasMaxLength(255);
        b.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(50);
        b.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(200);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
        b.Property(e => e.ReadAt).HasColumnName("read_at");
        b.Property(e => e.ClickedAt).HasColumnName("clicked_at");
        b.Property(e => e.FailureCode).HasColumnName("failure_code").HasMaxLength(50);
        b.Property(e => e.FailureMessage).HasColumnName("failure_message");
        b.Property(e => e.Cost).HasColumnName("cost").HasColumnType("numeric");
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_notifications_log_brand_id_fk");
        b.HasIndex(e => e.OutboxId).HasDatabaseName("idx_notifications_log_outbox_id_fk");
        b.HasIndex(e => new { e.BrandId, e.SentAt }).HasDatabaseName("idx_notiflog_brand_time");
        b.HasIndex(e => new { e.Provider, e.ProviderMessageId }).HasDatabaseName("idx_notiflog_provider");
        b.HasIndex(e => new { e.RecipientType, e.RecipientId, e.SentAt }).HasDatabaseName("idx_notiflog_recipient");
        b.HasIndex(e => new { e.ReferenceType, e.ReferenceId }).HasDatabaseName("idx_notiflog_reference");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("notifications_log_brand_id_fkey");

        b.HasOne(e => e.Outbox)
            .WithMany(o => o.NotificationLogs)
            .HasForeignKey(e => e.OutboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("notifications_log_outbox_id_fkey");
    }
}
