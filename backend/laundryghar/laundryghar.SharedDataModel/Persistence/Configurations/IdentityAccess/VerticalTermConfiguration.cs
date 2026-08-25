using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class VerticalTermConfiguration : IEntityTypeConfiguration<VerticalTerm>
{
    public void Configure(EntityTypeBuilder<VerticalTerm> b)
    {
        b.ToTable("vertical_terms", "identity_access");
        b.HasKey(e => new { e.VerticalKey, e.TermKey });
        b.Property(e => e.VerticalKey).HasColumnName("vertical_key").HasMaxLength(20).IsRequired();
        b.Property(e => e.TermKey).HasColumnName("term_key").HasMaxLength(48).IsRequired();
        b.Property(e => e.Singular).HasColumnName("singular").HasMaxLength(64).IsRequired();
        b.Property(e => e.Plural).HasColumnName("plural").HasMaxLength(64);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
