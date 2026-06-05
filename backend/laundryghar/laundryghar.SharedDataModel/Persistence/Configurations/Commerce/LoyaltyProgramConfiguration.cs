using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class LoyaltyProgramConfiguration : IEntityTypeConfiguration<LoyaltyProgram>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgram> b)
    {
        b.ToTable("loyalty_programs", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.EarnRate).HasColumnName("earn_rate").HasColumnType("numeric(8,4)").IsRequired();
        b.Property(e => e.EarnBasis).HasColumnName("earn_basis").HasMaxLength(20).IsRequired();
        b.Property(e => e.BurnRate).HasColumnName("burn_rate").HasColumnType("numeric(8,4)").IsRequired();
        b.Property(e => e.MinBurnPoints).HasColumnName("min_burn_points").IsRequired();
        b.Property(e => e.MaxBurnPerOrderPct).HasColumnName("max_burn_per_order_pct").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.MinOrderForEarn).HasColumnName("min_order_for_earn").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ExcludedServices).HasColumnName("excluded_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.PointExpiryMonths).HasColumnName("point_expiry_months");
        b.Property(e => e.WelcomeBonus).HasColumnName("welcome_bonus").IsRequired();
        b.Property(e => e.ReferralBonusReferrer).HasColumnName("referral_bonus_referrer").IsRequired();
        b.Property(e => e.ReferralBonusReferee).HasColumnName("referral_bonus_referee").IsRequired();
        b.Property(e => e.BirthdayBonus).HasColumnName("birthday_bonus").IsRequired();
        b.Property(e => e.TierConfig).HasColumnName("tier_config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Terms).HasColumnName("terms");
        b.Property(e => e.LaunchedAt).HasColumnName("launched_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // One programme per brand
        b.HasIndex(e => e.BrandId)
            .IsUnique()
            .HasDatabaseName("loyalty_programs_brand_id_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("loyalty_programs_brand_id_fkey");
    }
}
