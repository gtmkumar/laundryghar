using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> b)
    {
        b.ToTable("partners", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        b.Property(e => e.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.ContactEmail).HasColumnName("contact_email").HasMaxLength(255);
        b.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => e.Code).IsUnique().HasDatabaseName("partners_code_key");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
