using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("expense_categories", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ParentId).HasColumnName("parent_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsTaxDeductible).HasColumnName("is_tax_deductible").IsRequired();
        b.Property(e => e.RequiresApproval).HasColumnName("requires_approval").IsRequired();
        b.Property(e => e.ApprovalThreshold).HasColumnName("approval_threshold").HasColumnType("numeric(14,2)");
        b.Property(e => e.AccountingCode).HasColumnName("accounting_code").HasMaxLength(50);
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("expense_categories_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expense_categories_brand_id_fkey");

        // Self-referencing hierarchy — no explicit ON DELETE (defaults to NO ACTION)
        b.HasOne(e => e.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("expense_categories_parent_id_fkey");
    }
}
