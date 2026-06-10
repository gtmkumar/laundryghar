using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").IsRequired();
        b.Property(e => e.InvoiceDate).HasColumnName("invoice_date").HasColumnType("date").IsRequired();

        // Supplier snapshot
        b.Property(e => e.SupplierName).HasColumnName("supplier_name").IsRequired();
        b.Property(e => e.SupplierAddress).HasColumnName("supplier_address").IsRequired();
        b.Property(e => e.SupplierGstin).HasColumnName("supplier_gstin").HasMaxLength(15);

        // Customer snapshot
        b.Property(e => e.CustomerName).HasColumnName("customer_name").IsRequired();
        b.Property(e => e.CustomerPhone).HasColumnName("customer_phone").IsRequired();
        b.Property(e => e.CustomerGstin).HasColumnName("customer_gstin").HasMaxLength(15);

        // GST classification
        b.Property(e => e.PlaceOfSupply).HasColumnName("place_of_supply").IsRequired();
        b.Property(e => e.SacCode).HasColumnName("sac_code").HasMaxLength(10).IsRequired();
        b.Property(e => e.LineItems).HasColumnName("line_items").HasColumnType("jsonb").IsRequired();

        // Totals
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DiscountTotal).HasColumnName("discount_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxableTotal).HasColumnName("taxable_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CgstRate).HasColumnName("cgst_rate").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.CgstAmount).HasColumnName("cgst_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.SgstRate).HasColumnName("sgst_rate").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.SgstAmount).HasColumnName("sgst_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.IgstRate).HasColumnName("igst_rate").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.IgstAmount).HasColumnName("igst_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RoundOff).HasColumnName("round_off").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.OrderId).IsUnique().HasDatabaseName("invoices_order_id_key");
        b.HasIndex(e => new { e.BrandId, e.InvoiceNumber }).IsUnique()
            .HasDatabaseName("invoices_invoice_number_brand_key");
        b.HasIndex(e => new { e.BrandId, e.InvoiceDate })
            .HasDatabaseName("idx_invoices_brand_date");
    }
}
