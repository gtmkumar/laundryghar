using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("notification_templates", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).IsRequired();
        b.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        b.Property(e => e.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
        b.Property(e => e.SubjectTemplate).HasColumnName("subject_template").HasMaxLength(500);
        b.Property(e => e.BodyTemplate).HasColumnName("body_template").IsRequired();
        b.Property(e => e.SmsSenderId).HasColumnName("sms_sender_id").HasMaxLength(20);
        b.Property(e => e.WhatsAppTemplateName).HasColumnName("whatsapp_template_name").HasMaxLength(200);
        b.Property(e => e.WhatsAppTemplateId).HasColumnName("whatsapp_template_id").HasMaxLength(200);
        b.Property(e => e.WhatsAppLangCode).HasColumnName("whatsapp_lang_code").HasMaxLength(20);
        b.Property(e => e.WhatsAppNamespace).HasColumnName("whatsapp_namespace").HasMaxLength(100);
        b.Property(e => e.PushTitleTemplate).HasColumnName("push_title_template").HasMaxLength(200);
        b.Property(e => e.PushActionDeeplink).HasColumnName("push_action_deeplink");
        b.Property(e => e.PushIconUrl).HasColumnName("push_icon_url");
        b.Property(e => e.PushSound).HasColumnName("push_sound").HasMaxLength(50);
        b.Property(e => e.Variables).HasColumnName("variables").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.VersionNumber).HasColumnName("version_number").IsRequired();
        b.Property(e => e.ParentTemplateId).HasColumnName("parent_template_id");
        b.Property(e => e.IsTransactional).HasColumnName("is_transactional").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // Unique index: brand_id + code + channel + locale + version_number
        b.HasIndex(e => new { e.BrandId, e.Code, e.Channel, e.Locale, e.VersionNumber })
            .IsUnique()
            .HasDatabaseName("notification_templates_brand_id_code_channel_locale_version_key");

        // Non-unique lookup index
        b.HasIndex(e => new { e.BrandId, e.Code, e.Channel, e.Locale })
            .HasDatabaseName("idx_notiftpl_lookup");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("notification_templates_brand_id_fkey");

        b.HasOne(e => e.ParentTemplate)
            .WithMany(t => t.ChildTemplates)
            .HasForeignKey(e => e.ParentTemplateId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("notification_templates_parent_template_id_fkey");
    }
}
