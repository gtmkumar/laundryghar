using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderPayoutRequestConfiguration : IEntityTypeConfiguration<RiderPayoutRequest>
{
    public void Configure(EntityTypeBuilder<RiderPayoutRequest> b)
    {
        b.ToTable("rider_payout_requests", "logistics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        b.Property(e => e.PaymentReference).HasColumnName("payment_reference");
        b.Property(e => e.RequestedAt).HasColumnName("requested_at").IsRequired();
        b.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
        b.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(e => e.PaidBy).HasColumnName("paid_by");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasOne(e => e.Rider).WithMany().HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_payout_requests_rider_id_fkey");
    }
}

public sealed class IncentiveRuleConfiguration : IEntityTypeConfiguration<IncentiveRule>
{
    public void Configure(EntityTypeBuilder<IncentiveRule> b)
    {
        b.ToTable("incentive_rules", "logistics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(e => e.RuleType).HasColumnName("rule_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Threshold).HasColumnName("threshold").IsRequired();
        b.Property(e => e.RewardAmount).HasColumnName("reward_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Window).HasColumnName("window").HasMaxLength(20).IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.Property(e => e.ValidUntil).HasColumnName("valid_until");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
    }
}

public sealed class RiderIncentiveAwardConfiguration : IEntityTypeConfiguration<RiderIncentiveAward>
{
    public void Configure(EntityTypeBuilder<RiderIncentiveAward> b)
    {
        b.ToTable("rider_incentive_awards", "logistics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.RuleId).HasColumnName("rule_id");
        b.Property(e => e.RuleNameSnapshot).HasColumnName("rule_name_snapshot").HasMaxLength(120).IsRequired();
        b.Property(e => e.RuleType).HasColumnName("rule_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.PeriodKey).HasColumnName("period_key").HasMaxLength(20).IsRequired();
        b.Property(e => e.DeliveryAssignmentId).HasColumnName("delivery_assignment_id");
        b.Property(e => e.AwardedAt).HasColumnName("awarded_at").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Rider).WithMany().HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_incentive_awards_rider_id_fkey");
        b.HasOne(e => e.Rule).WithMany().HasForeignKey(e => e.RuleId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("rider_incentive_awards_rule_id_fkey");
    }
}
