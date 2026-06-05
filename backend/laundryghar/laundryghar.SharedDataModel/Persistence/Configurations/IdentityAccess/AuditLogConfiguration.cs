using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs", "identity_access");

        // Composite PK required by PG range partitioning on occurred_at.
        b.HasKey(e => new { e.Id, e.OccurredAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
        b.Property(e => e.ActorCustomerId).HasColumnName("actor_customer_id");
        b.Property(e => e.ActorType).HasColumnName("actor_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ActorDisplay).HasColumnName("actor_display").HasMaxLength(200);
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        b.Property(e => e.ResourceType).HasColumnName("resource_type").HasMaxLength(50).IsRequired();
        b.Property(e => e.ResourceId).HasColumnName("resource_id");
        b.Property(e => e.ResourceDisplay).HasColumnName("resource_display").HasMaxLength(200);
        b.Property(e => e.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        b.Property(e => e.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        b.Property(e => e.ChangedFields).HasColumnName("changed_fields").HasColumnType("text[]");
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.RequestId).HasColumnName("request_id");
        b.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        b.Property(e => e.Success).HasColumnName("success").IsRequired();
        b.Property(e => e.ErrorMessage).HasColumnName("error_message");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // FK navigations within this library's scope
        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("audit_logs_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("audit_logs_franchise_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("audit_logs_store_id_fkey");

        b.HasOne(e => e.Warehouse)
            .WithMany()
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("audit_logs_warehouse_id_fkey");
    }
}
