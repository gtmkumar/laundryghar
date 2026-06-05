using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class CustomerDeviceConfiguration : IEntityTypeConfiguration<CustomerDevice>
{
    public void Configure(EntityTypeBuilder<CustomerDevice> b)
    {
        b.ToTable("customer_devices", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255).IsRequired();
        b.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(20).IsRequired();
        b.Property(e => e.OsVersion).HasColumnName("os_version").HasMaxLength(50);
        b.Property(e => e.DeviceModel).HasColumnName("device_model").HasMaxLength(100);
        b.Property(e => e.DeviceName).HasColumnName("device_name").HasMaxLength(200);
        b.Property(e => e.AppVersion).HasColumnName("app_version").HasMaxLength(20);
        b.Property(e => e.AppBuild).HasColumnName("app_build").HasMaxLength(50);
        b.Property(e => e.FcmToken).HasColumnName("fcm_token");
        b.Property(e => e.ApnsToken).HasColumnName("apns_token");
        b.Property(e => e.PushEnabled).HasColumnName("push_enabled").IsRequired();
        b.Property(e => e.Language).HasColumnName("language").HasMaxLength(10);
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50);
        b.Property(e => e.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        b.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => new { e.CustomerId, e.DeviceId }).IsUnique().HasDatabaseName("customer_devices_customer_id_device_id_key");

        b.HasOne(e => e.Customer)
            .WithMany(c => c.Devices)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("customer_devices_customer_id_fkey");
    }
}
