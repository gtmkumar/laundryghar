using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public class BrandCancellationConfiguration : IEntityTypeConfiguration<BrandCancellation>
{
    public void Configure(EntityTypeBuilder<BrandCancellation> b)
    {
        b.ToTable("brand_cancellations", "tenancy_org");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
        b.Property(e => e.Reason).HasColumnName("reason");
        b.Property(e => e.RequestedAt).HasColumnName("requested_at");
        b.Property(e => e.RetentionUntil).HasColumnName("retention_until");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(e => e.WithdrawnAt).HasColumnName("withdrawn_at");
        b.Property(e => e.WithdrawnByUserId).HasColumnName("withdrawn_by_user_id");
        b.Property(e => e.PurgedAt).HasColumnName("purged_at");
        b.Property(e => e.ExportCount).HasColumnName("export_count");
        b.Property(e => e.LastExportedAt).HasColumnName("last_exported_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
    }
}
