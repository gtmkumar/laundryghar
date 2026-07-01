using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class PartnerUserConfiguration : IEntityTypeConfiguration<PartnerUser>
{
    public void Configure(EntityTypeBuilder<PartnerUser> b)
    {
        b.ToTable("partner_users", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.PhoneE164).HasColumnName("phone_e164").HasMaxLength(20);
        b.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
        b.Property(e => e.PartnerRole).HasColumnName("partner_role").HasMaxLength(20).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.PartnerId).HasDatabaseName("idx_partner_users_partner");

        b.HasOne(e => e.Partner).WithMany(p => p.Users).HasForeignKey(e => e.PartnerId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("partner_users_partner_id_fkey");
    }
}
