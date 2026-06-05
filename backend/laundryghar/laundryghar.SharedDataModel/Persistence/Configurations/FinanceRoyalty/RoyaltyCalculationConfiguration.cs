using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class RoyaltyCalculationConfiguration : IEntityTypeConfiguration<RoyaltyCalculation>
{
    public void Configure(EntityTypeBuilder<RoyaltyCalculation> b)
    {
        b.ToTable("royalty_calculations", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.RoyaltyInvoiceId).HasColumnName("royalty_invoice_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.CalculationDate).HasColumnName("calculation_date").HasColumnType("date").IsRequired();
        b.Property(e => e.ServiceCategoryId).HasColumnName("service_category_id");
        b.Property(e => e.RevenueType).HasColumnName("revenue_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.GrossAmount).HasColumnName("gross_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ExcludedAmount).HasColumnName("excluded_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ExclusionReason).HasColumnName("exclusion_reason").HasMaxLength(100);
        b.Property(e => e.EligibleAmount).HasColumnName("eligible_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RoyaltyRate).HasColumnName("royalty_rate").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.RoyaltyAmount).HasColumnName("royalty_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.RoyaltyInvoice)
            .WithMany(ri => ri.Calculations)
            .HasForeignKey(e => e.RoyaltyInvoiceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("royalty_calculations_royalty_invoice_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_calculations_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_calculations_franchise_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_calculations_store_id_fkey");

        b.HasOne(e => e.ServiceCategory)
            .WithMany()
            .HasForeignKey(e => e.ServiceCategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_calculations_service_category_id_fkey");

        // Composite FK to partitioned orders — scalar-only (both columns present)
        b.HasOne<Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("royalty_calculations_order_id_fkey");
    }
}
