using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty.Subscriptions;

/// <summary>Append-only lifecycle events — no updates.</summary>
public sealed class FranchiseSubscriptionEventConfiguration : IEntityTypeConfiguration<FranchiseSubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<FranchiseSubscriptionEvent> b)
    {
        b.ToTable("franchise_subscription_events", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseSubscriptionId).HasColumnName("franchise_subscription_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.FromPlanId).HasColumnName("from_plan_id");
        b.Property(e => e.ToPlanId).HasColumnName("to_plan_id");
        b.Property(e => e.FromStatus).HasColumnName("from_status").HasMaxLength(20);
        b.Property(e => e.ToStatus).HasColumnName("to_status").HasMaxLength(20);
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(200);
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.ActorType).HasColumnName("actor_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.ActorId).HasColumnName("actor_id");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        b.HasOne(e => e.FranchiseSubscription)
            .WithMany(fs => fs.Events)
            .HasForeignKey(e => e.FranchiseSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("franchise_subscription_events_franchise_subscription_id_fkey");
    }
}
