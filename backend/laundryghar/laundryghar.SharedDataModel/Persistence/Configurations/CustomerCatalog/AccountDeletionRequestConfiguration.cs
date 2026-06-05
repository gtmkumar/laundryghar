using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class AccountDeletionRequestConfiguration : IEntityTypeConfiguration<AccountDeletionRequest>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRequest> b)
    {
        b.ToTable("account_deletion_requests", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.RequestSource).HasColumnName("request_source").HasMaxLength(20).IsRequired();
        b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(50);
        b.Property(e => e.ReasonText).HasColumnName("reason_text");
        b.Property(e => e.RequestedAt).HasColumnName("requested_at").IsRequired();
        b.Property(e => e.GracePeriodEndsAt).HasColumnName("grace_period_ends_at").IsRequired();
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancelledReason).HasColumnName("cancelled_reason");
        b.Property(e => e.SoftDeletedAt).HasColumnName("soft_deleted_at");
        b.Property(e => e.HardDeletedAt).HasColumnName("hard_deleted_at");
        b.Property(e => e.AnonymizedAt).HasColumnName("anonymized_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.PendingOrdersCount).HasColumnName("pending_orders_count").IsRequired();
        b.Property(e => e.PendingAmount).HasColumnName("pending_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DataExportUrl).HasColumnName("data_export_url");
        b.Property(e => e.DataExportExpiresAt).HasColumnName("data_export_expires_at");
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.ProcessedBy).HasColumnName("processed_by");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Customer)
            .WithMany(c => c.AccountDeletionRequests)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("account_deletion_requests_customer_id_fkey");

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("account_deletion_requests_user_id_fkey");
    }
}
