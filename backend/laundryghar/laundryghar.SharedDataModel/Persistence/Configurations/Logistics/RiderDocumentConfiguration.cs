using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderDocumentConfiguration : IEntityTypeConfiguration<RiderDocument>
{
    public void Configure(EntityTypeBuilder<RiderDocument> b)
    {
        b.ToTable("rider_documents", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.DocType).HasColumnName("doc_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.StorageKey).HasColumnName("storage_key").IsRequired();
        b.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
        b.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
        b.Property(e => e.Bytes).HasColumnName("bytes").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        b.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
        b.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.RiderId, e.DocType }).HasDatabaseName("idx_rider_documents_rider");

        b.HasOne(e => e.Rider).WithMany(r => r.Documents).HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_documents_rider_id_fkey");
    }
}
