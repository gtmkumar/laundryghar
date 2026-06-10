using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class NotificationEventCursorConfiguration : IEntityTypeConfiguration<NotificationEventCursor>
{
    public void Configure(EntityTypeBuilder<NotificationEventCursor> b)
    {
        b.ToTable("notification_event_cursors", "engagement_cms");

        b.HasKey(e => e.ConsumerName);
        b.Property(e => e.ConsumerName).HasColumnName("consumer_name").HasMaxLength(100).IsRequired();
        b.Property(e => e.LastEventId).HasColumnName("last_event_id");
        b.Property(e => e.ProcessedCount).HasColumnName("processed_count").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
