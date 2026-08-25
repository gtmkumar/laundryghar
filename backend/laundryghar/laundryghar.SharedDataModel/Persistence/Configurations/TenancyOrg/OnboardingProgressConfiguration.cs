using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public class OnboardingProgressConfiguration : IEntityTypeConfiguration<OnboardingProgress>
{
    public void Configure(EntityTypeBuilder<OnboardingProgress> b)
    {
        b.ToTable("onboarding_progress", "tenancy_org");
        b.HasKey(e => e.BrandId);

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.SkippedSteps).HasColumnName("skipped_steps");
        b.Property(e => e.Draft).HasColumnName("draft").HasColumnType("jsonb");
        b.Property(e => e.StartedAt).HasColumnName("started_at");
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
    }
}
