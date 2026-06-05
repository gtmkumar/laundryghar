using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class RoyaltyInvoiceConfiguration : IEntityTypeConfiguration<RoyaltyInvoice>
{
    public void Configure(EntityTypeBuilder<RoyaltyInvoice> b)
    {
        b.ToTable("royalty_invoices", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.FranchiseAgreementId).HasColumnName("franchise_agreement_id");
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        b.Property(e => e.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();
        b.Property(e => e.GrossRevenue).HasColumnName("gross_revenue").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.EligibleRevenue).HasColumnName("eligible_revenue").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RoyaltyPercent).HasColumnName("royalty_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.RoyaltyAmount).HasColumnName("royalty_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.MarketingFeePercent).HasColumnName("marketing_fee_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.MarketingFeeAmount).HasColumnName("marketing_fee_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TechnologyFeeAmount).HasColumnName("technology_fee_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OtherCharges).HasColumnName("other_charges").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Adjustments).HasColumnName("adjustments").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Cgst).HasColumnName("cgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Sgst).HasColumnName("sgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Igst).HasColumnName("igst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountPaid).HasColumnName("amount_paid").HasColumnType("numeric(14,2)").IsRequired();

        // Generated column — EF must not attempt to write it
        b.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();

        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.TotalOrders).HasColumnName("total_orders").IsRequired();
        b.Property(e => e.InvoiceDate).HasColumnName("invoice_date").HasColumnType("date").IsRequired();
        b.Property(e => e.DueDate).HasColumnName("due_date").HasColumnType("date").IsRequired();
        b.Property(e => e.SentAt).HasColumnName("sent_at");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.InvoiceS3Key).HasColumnName("invoice_s3_key");
        b.Property(e => e.InvoicePdfUrl).HasColumnName("invoice_pdf_url");
        b.Property(e => e.LineItems).HasColumnName("line_items").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.DisputedAt).HasColumnName("disputed_at");
        b.Property(e => e.DisputeReason).HasColumnName("dispute_reason");
        b.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.FranchiseId, e.PeriodStart, e.PeriodEnd })
            .IsUnique()
            .HasDatabaseName("royalty_invoices_franchise_id_period_start_period_end_key");

        b.HasIndex(e => e.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("royalty_invoices_invoice_number_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_invoices_brand_id_fkey");

        // No explicit ON DELETE on franchise FK (defaults to NO ACTION)
        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("royalty_invoices_franchise_id_fkey");

        // No explicit ON DELETE on franchise_agreement FK (defaults to NO ACTION)
        b.HasOne(e => e.FranchiseAgreement)
            .WithMany()
            .HasForeignKey(e => e.FranchiseAgreementId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("royalty_invoices_franchise_agreement_id_fkey");
    }
}
