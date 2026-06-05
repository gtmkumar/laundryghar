using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class WhatsAppMessageLogConfiguration : IEntityTypeConfiguration<WhatsAppMessageLog>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessageLog> b)
    {
        b.ToTable("whatsapp_message_log", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.PhoneE164).HasColumnName("phone_e164").HasMaxLength(20).IsRequired();
        b.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        b.Property(e => e.WaMessageId).HasColumnName("wa_message_id").HasMaxLength(200);
        b.Property(e => e.WaConversationId).HasColumnName("wa_conversation_id").HasMaxLength(200);
        b.Property(e => e.TemplateName).HasColumnName("template_name").HasMaxLength(200);
        b.Property(e => e.MessageType).HasColumnName("message_type").HasMaxLength(20);
        b.Property(e => e.BodyText).HasColumnName("body_text");
        b.Property(e => e.MediaS3Key).HasColumnName("media_s3_key");
        b.Property(e => e.MediaMimeType).HasColumnName("media_mime_type").HasMaxLength(100);
        b.Property(e => e.ButtonPayload).HasColumnName("button_payload").HasMaxLength(500);
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(50);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(e => e.SentAt).HasColumnName("sent_at").IsRequired();
        b.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
        b.Property(e => e.ReadAt).HasColumnName("read_at");
        b.Property(e => e.FailedAt).HasColumnName("failed_at");
        b.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(50);
        b.Property(e => e.ErrorMessage).HasColumnName("error_message");
        b.Property(e => e.CostUnits).HasColumnName("cost_units").HasColumnType("numeric");
        b.Property(e => e.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.WaMessageId)
            .IsUnique()
            .HasDatabaseName("whatsapp_message_log_wa_message_id_key");

        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_whatsapp_message_log_brand_id_fk");
        b.HasIndex(e => e.CustomerId).HasDatabaseName("idx_whatsapp_message_log_customer_id_fk");
        b.HasIndex(e => e.UserId).HasDatabaseName("idx_whatsapp_message_log_user_id_fk");
        b.HasIndex(e => new { e.CustomerId, e.SentAt }).HasDatabaseName("idx_walog_customer");
        b.HasIndex(e => new { e.PhoneE164, e.SentAt }).HasDatabaseName("idx_walog_phone_time");
        b.HasIndex(e => new { e.ReferenceType, e.ReferenceId }).HasDatabaseName("idx_walog_reference");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("whatsapp_message_log_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("whatsapp_message_log_customer_id_fkey");

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("whatsapp_message_log_user_id_fkey");
    }
}
