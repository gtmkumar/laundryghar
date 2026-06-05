using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class LoyaltyPointsLedgerConfiguration : IEntityTypeConfiguration<LoyaltyPointsLedger>
{
    public void Configure(EntityTypeBuilder<LoyaltyPointsLedger> b)
    {
        b.ToTable("loyalty_points_ledger", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.LoyaltyProgramId).HasColumnName("loyalty_program_id").IsRequired();
        b.Property(e => e.TransactionType).HasColumnName("transaction_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        b.Property(e => e.Points).HasColumnName("points").IsRequired();
        b.Property(e => e.BalanceBefore).HasColumnName("balance_before").IsRequired();
        b.Property(e => e.BalanceAfter).HasColumnName("balance_after").IsRequired();
        b.Property(e => e.MonetaryEquivalent).HasColumnName("monetary_equivalent").HasColumnType("numeric(14,2)");
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.PerformedBy).HasColumnName("performed_by");
        b.Property(e => e.PerformedByType).HasColumnName("performed_by_type").HasMaxLength(20);
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("loyalty_points_ledger_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("loyalty_points_ledger_customer_id_fkey");

        b.HasOne(e => e.LoyaltyProgram)
            .WithMany(lp => lp.PointsLedgerEntries)
            .HasForeignKey(e => e.LoyaltyProgramId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("loyalty_points_ledger_loyalty_program_id_fkey");

        // Composite FK to partitioned orders — scalar-only
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("loyalty_points_ledger_order_id_fkey");
    }
}
