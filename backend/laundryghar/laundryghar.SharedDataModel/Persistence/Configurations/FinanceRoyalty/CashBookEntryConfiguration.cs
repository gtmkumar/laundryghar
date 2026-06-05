using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class CashBookEntryConfiguration : IEntityTypeConfiguration<CashBookEntry>
{
    public void Configure(EntityTypeBuilder<CashBookEntry> b)
    {
        b.ToTable("cash_book_entries", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CashBookId).HasColumnName("cash_book_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.EntryType).HasColumnName("entry_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.PaymentMode).HasColumnName("payment_mode").HasMaxLength(20).IsRequired();
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.ExpenseId).HasColumnName("expense_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.PayeeName).HasColumnName("payee_name").HasMaxLength(200);
        b.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(e => e.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(100);
        b.Property(e => e.ReceiptS3Key).HasColumnName("receipt_s3_key");
        b.Property(e => e.PerformedBy).HasColumnName("performed_by").IsRequired();
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(e => e.ReversedAt).HasColumnName("reversed_at");
        b.Property(e => e.ReversedBy).HasColumnName("reversed_by");
        b.Property(e => e.ReversedReason).HasColumnName("reversed_reason");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.CashBook)
            .WithMany(cb => cb.Entries)
            .HasForeignKey(e => e.CashBookId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_book_entries_cash_book_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_book_entries_brand_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_book_entries_store_id_fkey");

        b.HasOne(e => e.Expense)
            .WithMany()
            .HasForeignKey(e => e.ExpenseId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("cash_book_entries_expense_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_book_entries_customer_id_fkey");

        // Composite FK to partitioned orders — scalar-only (both columns present)
        b.HasOne<Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("cash_book_entries_order_id_fkey");
    }
}
