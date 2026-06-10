using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

public sealed class PaymentMandateConfiguration : IEntityTypeConfiguration<PaymentMandate>
{
    public void Configure(EntityTypeBuilder<PaymentMandate> b)
    {
        b.ToTable("payment_mandates", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.MandateType).HasColumnName("mandate_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30).IsRequired();
        b.Property(e => e.GatewayMandateId).HasColumnName("gateway_mandate_id").HasMaxLength(100);
        b.Property(e => e.GatewayToken).HasColumnName("gateway_token").HasMaxLength(200);
        b.Property(e => e.GatewayCustomerId).HasColumnName("gateway_customer_id").HasMaxLength(100);
        b.Property(e => e.MaxAmount).HasColumnName("max_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DebitFrequency).HasColumnName("debit_frequency").HasMaxLength(20).IsRequired();
        b.Property(e => e.UpiVpa).HasColumnName("upi_vpa").HasMaxLength(100);
        b.Property(e => e.CardLast4).HasColumnName("card_last4").HasColumnType("character(4)");
        b.Property(e => e.CardNetwork).HasColumnName("card_network").HasMaxLength(20);
        b.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.StartAt).HasColumnName("start_at");
        b.Property(e => e.EndAt).HasColumnName("end_at");
        b.Property(e => e.AuthenticatedAt).HasColumnName("authenticated_at");
        b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        b.Property(e => e.RevokedReason).HasColumnName("revoked_reason");
        b.Property(e => e.FailureCode).HasColumnName("failure_code").HasMaxLength(50);
        b.Property(e => e.FailureMessage).HasColumnName("failure_message");
        b.Property(e => e.GatewayResponse).HasColumnName("gateway_response").HasColumnType("jsonb");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("payment_mandates_customer_id_fkey");
    }
}
