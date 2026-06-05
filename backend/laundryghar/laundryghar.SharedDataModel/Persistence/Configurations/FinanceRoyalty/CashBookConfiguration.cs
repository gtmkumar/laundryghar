using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class CashBookConfiguration : IEntityTypeConfiguration<CashBook>
{
    public void Configure(EntityTypeBuilder<CashBook> b)
    {
        b.ToTable("cash_books", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.BookDate).HasColumnName("book_date").HasColumnType("date").IsRequired();
        b.Property(e => e.ShiftLabel).HasColumnName("shift_label").HasMaxLength(30).IsRequired();
        b.Property(e => e.OpeningUserId).HasColumnName("opening_user_id").IsRequired();
        b.Property(e => e.ClosingUserId).HasColumnName("closing_user_id");
        b.Property(e => e.OpeningBalance).HasColumnName("opening_balance").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ClosingBalance).HasColumnName("closing_balance").HasColumnType("numeric(14,2)");
        b.Property(e => e.ExpectedClosing).HasColumnName("expected_closing").HasColumnType("numeric(14,2)");

        // Generated column — EF must not attempt to write it
        b.Property(e => e.Variance).HasColumnName("variance").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();

        b.Property(e => e.CashInflow).HasColumnName("cash_inflow").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CashOutflow).HasColumnName("cash_outflow").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.UpiInflow).HasColumnName("upi_inflow").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CardInflow).HasColumnName("card_inflow").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OtherInflow).HasColumnName("other_inflow").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DepositReference).HasColumnName("deposit_reference").HasMaxLength(100);
        b.Property(e => e.TotalOrders).HasColumnName("total_orders").IsRequired();
        b.Property(e => e.NewOrders).HasColumnName("new_orders").IsRequired();
        b.Property(e => e.DeliveredOrders).HasColumnName("delivered_orders").IsRequired();
        b.Property(e => e.CancelledOrders).HasColumnName("cancelled_orders").IsRequired();
        b.Property(e => e.OpenedAt).HasColumnName("opened_at").IsRequired();
        b.Property(e => e.ClosedAt).HasColumnName("closed_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.VarianceReason).HasColumnName("variance_reason");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.StoreId, e.BookDate, e.ShiftLabel })
            .IsUnique()
            .HasDatabaseName("cash_books_store_id_book_date_shift_label_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_books_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_books_franchise_id_fkey");

        // store FK has no explicit ON DELETE in schema (defaults to NO ACTION)
        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("cash_books_store_id_fkey");
    }
}
