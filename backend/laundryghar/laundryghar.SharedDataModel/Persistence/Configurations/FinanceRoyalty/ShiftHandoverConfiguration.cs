using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class ShiftHandoverConfiguration : IEntityTypeConfiguration<ShiftHandover>
{
    public void Configure(EntityTypeBuilder<ShiftHandover> b)
    {
        b.ToTable("shift_handovers", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.FromUserId).HasColumnName("from_user_id").IsRequired();
        b.Property(e => e.ToUserId).HasColumnName("to_user_id");
        b.Property(e => e.CashBookId).HasColumnName("cash_book_id");
        b.Property(e => e.HandoverAt).HasColumnName("handover_at").IsRequired();
        b.Property(e => e.CashHandedOver).HasColumnName("cash_handed_over").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CashCountedByToUser).HasColumnName("cash_counted_by_to_user").HasColumnType("numeric(14,2)");

        // Generated column — EF must not attempt to write it
        b.Property(e => e.CashVariance).HasColumnName("cash_variance").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();

        b.Property(e => e.PendingOrdersCount).HasColumnName("pending_orders_count").IsRequired();
        b.Property(e => e.OpenComplaintsCount).HasColumnName("open_complaints_count").IsRequired();
        b.Property(e => e.PickupsRemaining).HasColumnName("pickups_remaining").IsRequired();
        b.Property(e => e.DeliveriesRemaining).HasColumnName("deliveries_remaining").IsRequired();
        b.Property(e => e.NotesFrom).HasColumnName("notes_from");
        b.Property(e => e.NotesTo).HasColumnName("notes_to");
        b.Property(e => e.PendingItems).HasColumnName("pending_items").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");
        b.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.DisputeReason).HasColumnName("dispute_reason");
        b.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
        b.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("shift_handovers_brand_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("shift_handovers_store_id_fkey");

        // cash_book FK has no explicit ON DELETE (defaults to NO ACTION)
        b.HasOne(e => e.CashBook)
            .WithMany(cb => cb.ShiftHandovers)
            .HasForeignKey(e => e.CashBookId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("shift_handovers_cash_book_id_fkey");

        // User FKs: no explicit ON DELETE on either (defaults to NO ACTION)
        b.HasOne(e => e.FromUser)
            .WithMany()
            .HasForeignKey(e => e.FromUserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("shift_handovers_from_user_id_fkey");

        b.HasOne(e => e.ToUser)
            .WithMany()
            .HasForeignKey(e => e.ToUserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("shift_handovers_to_user_id_fkey");
    }
}
