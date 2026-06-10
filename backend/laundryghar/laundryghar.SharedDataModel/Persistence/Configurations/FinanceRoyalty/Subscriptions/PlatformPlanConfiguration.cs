using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty.Subscriptions;

public sealed class PlatformPlanConfiguration : IEntityTypeConfiguration<PlatformPlan>
{
    public void Configure(EntityTypeBuilder<PlatformPlan> b)
    {
        b.ToTable("platform_plans", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.Tier).HasColumnName("tier").HasMaxLength(30).IsRequired();
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20).IsRequired();
        b.Property(e => e.IntervalCount).HasColumnName("interval_count").IsRequired();
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.SetupFee).HasColumnName("setup_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AnnualDiscountPercent).HasColumnName("annual_discount_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.TrialDays).HasColumnName("trial_days").IsRequired();
        b.Property(e => e.MaxStores).HasColumnName("max_stores");
        b.Property(e => e.MaxWarehouses).HasColumnName("max_warehouses");
        b.Property(e => e.MaxUsers).HasColumnName("max_users");
        b.Property(e => e.MaxOrdersPerMonth).HasColumnName("max_orders_per_month");
        b.Property(e => e.MaxRiders).HasColumnName("max_riders");
        b.Property(e => e.OveragePerOrder).HasColumnName("overage_per_order").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OveragePerStore).HasColumnName("overage_per_store").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OveragePerUser).HasColumnName("overage_per_user").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Features).HasColumnName("features").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.SupportLevel).HasColumnName("support_level").HasMaxLength(20).IsRequired();
        b.Property(e => e.IsPublic).HasColumnName("is_public").IsRequired();
        b.Property(e => e.IsFeatured).HasColumnName("is_featured").IsRequired();
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30);
        b.Property(e => e.GatewayPlanId).HasColumnName("gateway_plan_id").HasMaxLength(100);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        // UNIQUE(brand_id, code) — brand_id can be NULL (global plans)
        // Index defined in DB; EF does not model nullable unique easily, so skip EF index declaration
        // and rely on DB-level constraint.

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("platform_plans_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
