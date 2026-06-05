using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("order_status_history", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FromStatus).HasColumnName("from_status").HasMaxLength(30);
        b.Property(e => e.ToStatus).HasColumnName("to_status").HasMaxLength(30).IsRequired();
        b.Property(e => e.FromSubStatus).HasColumnName("from_sub_status").HasMaxLength(50);
        b.Property(e => e.ToSubStatus).HasColumnName("to_sub_status").HasMaxLength(50);
        b.Property(e => e.ChangedAt).HasColumnName("changed_at").IsRequired();
        b.Property(e => e.ChangedByType).HasColumnName("changed_by_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ChangedById).HasColumnName("changed_by_id");
        b.Property(e => e.ChangedByName).HasColumnName("changed_by_name").HasMaxLength(200);
        b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(200);
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CustomerNotified).HasColumnName("customer_notified").IsRequired();
        b.Property(e => e.NotificationChannels).HasColumnName("notification_channels").HasColumnType("text[]");
        b.Property(e => e.Location).HasColumnName("location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Composite FK to partitioned orders table
        b.HasOne(e => e.Order)
            .WithMany(o => o.StatusHistories)
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("order_status_history_order_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("order_status_history_brand_id_fkey");
    }
}
