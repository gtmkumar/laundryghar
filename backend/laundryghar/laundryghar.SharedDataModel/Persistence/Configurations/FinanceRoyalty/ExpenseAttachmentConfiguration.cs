using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty;

public sealed class ExpenseAttachmentConfiguration : IEntityTypeConfiguration<ExpenseAttachment>
{
    public void Configure(EntityTypeBuilder<ExpenseAttachment> b)
    {
        b.ToTable("expense_attachments", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.ExpenseId).HasColumnName("expense_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(e => e.ThumbnailS3Key).HasColumnName("thumbnail_s3_key");
        b.Property(e => e.CdnUrl).HasColumnName("cdn_url");
        b.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        b.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
        b.Property(e => e.Bytes).HasColumnName("bytes");
        b.Property(e => e.DocumentType).HasColumnName("document_type").HasMaxLength(30);
        b.Property(e => e.IsPrimary).HasColumnName("is_primary").IsRequired();
        b.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
        b.Property(e => e.UploadedAt).HasColumnName("uploaded_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasQueryFilter(e => e.DeletedAt == null);

        b.HasOne(e => e.Expense)
            .WithMany(ex => ex.Attachments)
            .HasForeignKey(e => e.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("expense_attachments_expense_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("expense_attachments_brand_id_fkey");
    }
}
