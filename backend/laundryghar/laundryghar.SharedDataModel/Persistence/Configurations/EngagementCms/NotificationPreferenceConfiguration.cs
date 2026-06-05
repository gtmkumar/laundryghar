using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.NotificationCategory).HasColumnName("notification_category").HasMaxLength(50).IsRequired();
        b.Property(e => e.SmsEnabled).HasColumnName("sms_enabled").IsRequired();
        b.Property(e => e.WhatsAppEnabled).HasColumnName("whatsapp_enabled").IsRequired();
        b.Property(e => e.EmailEnabled).HasColumnName("email_enabled").IsRequired();
        b.Property(e => e.PushEnabled).HasColumnName("push_enabled").IsRequired();
        b.Property(e => e.InAppEnabled).HasColumnName("in_app_enabled").IsRequired();
        b.Property(e => e.VoiceEnabled).HasColumnName("voice_enabled").IsRequired();
        b.Property(e => e.QuietHoursStart).HasColumnName("quiet_hours_start").HasColumnType("time without time zone");
        b.Property(e => e.QuietHoursEnd).HasColumnName("quiet_hours_end").HasColumnType("time without time zone");
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50);
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // Unique: one preference row per (customer_id, notification_category)
        b.HasIndex(e => new { e.CustomerId, e.NotificationCategory })
            .IsUnique()
            .HasDatabaseName("notification_preferences_customer_id_notification_category_key");

        // Unique: one preference row per (user_id, notification_category)
        b.HasIndex(e => new { e.UserId, e.NotificationCategory })
            .IsUnique()
            .HasDatabaseName("notification_preferences_user_id_notification_category_key");

        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_notification_preferences_brand_id_fk");
        b.HasIndex(e => e.CustomerId).HasDatabaseName("idx_notifpref_customer");
        b.HasIndex(e => e.UserId).HasDatabaseName("idx_notifpref_user");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("notification_preferences_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("notification_preferences_customer_id_fkey");

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("notification_preferences_user_id_fkey");
    }
}
