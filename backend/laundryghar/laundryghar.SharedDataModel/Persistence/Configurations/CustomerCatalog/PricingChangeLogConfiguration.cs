using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class PricingChangeLogConfiguration : IEntityTypeConfiguration<PricingChangeLog>
{
    public void Configure(EntityTypeBuilder<PricingChangeLog> b)
    {
        b.ToTable("pricing_change_log", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.TargetKind).HasColumnName("target_kind").HasMaxLength(40).IsRequired();
        b.Property(e => e.TargetId).HasColumnName("target_id").IsRequired();
        b.Property(e => e.Summary).HasColumnName("summary").IsRequired();
        b.Property(e => e.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        b.Property(e => e.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        b.Property(e => e.ActorId).HasColumnName("actor_id");
        b.Property(e => e.ActorName).HasColumnName("actor_name").HasMaxLength(200);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.RevertedAt).HasColumnName("reverted_at");
        b.Property(e => e.RevertedBy).HasColumnName("reverted_by");

        b.HasIndex(e => new { e.BrandId, e.CreatedAt });
    }
}
