using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.CategoryId).HasColumnName("category_id").IsRequired();
        b.Property(e => e.CashBookEntryId).HasColumnName("cash_book_entry_id");
        b.Property(e => e.ExpenseNumber).HasColumnName("expense_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.ExpenseDate).HasColumnName("expense_date").HasColumnType("date").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxAmount).HasColumnName("tax_amount").HasColumnType("numeric(14,2)").IsRequired();

        // Generated column — EF must not attempt to write it
        b.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();

        b.Property(e => e.PaymentMode).HasColumnName("payment_mode").HasMaxLength(20).IsRequired();
        b.Property(e => e.VendorName).HasColumnName("vendor_name").HasMaxLength(200);
        b.Property(e => e.VendorGstin).HasColumnName("vendor_gstin").HasMaxLength(15);
        b.Property(e => e.VendorPhone).HasColumnName("vendor_phone").HasMaxLength(20);
        b.Property(e => e.BillNumber).HasColumnName("bill_number").HasMaxLength(100);
        b.Property(e => e.BillDate).HasColumnName("bill_date").HasColumnType("date");
        b.Property(e => e.Description).HasColumnName("description").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.IsRecurring).HasColumnName("is_recurring").IsRequired();
        b.Property(e => e.RecurrenceFrequency).HasColumnName("recurrence_frequency").HasMaxLength(20);
        b.Property(e => e.IsReimbursable).HasColumnName("is_reimbursable").IsRequired();
        b.Property(e => e.SubmittedBy).HasColumnName("submitted_by").IsRequired();
        b.Property(e => e.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        b.Property(e => e.RequiresApproval).HasColumnName("requires_approval").IsRequired();
        b.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.RejectedBy).HasColumnName("rejected_by");
        b.Property(e => e.RejectedAt).HasColumnName("rejected_at");
        b.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.ExpenseNumber)
            .IsUnique()
            .HasDatabaseName("expenses_expense_number_key");

        b.HasQueryFilter(e => e.DeletedAt == null);

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expenses_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expenses_franchise_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expenses_store_id_fkey");

        b.HasOne(e => e.Warehouse)
            .WithMany()
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expenses_warehouse_id_fkey");

        // No explicit ON DELETE on category FK (defaults to NO ACTION)
        b.HasOne(e => e.Category)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("expenses_category_id_fkey");

        b.HasOne(e => e.CashBookEntry)
            .WithMany()
            .HasForeignKey(e => e.CashBookEntryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expenses_cash_book_entry_id_fkey");
    }
}
