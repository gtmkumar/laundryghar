using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class OrderNoteConfiguration : IEntityTypeConfiguration<OrderNote>
{
    public void Configure(EntityTypeBuilder<OrderNote> b)
    {
        b.ToTable("order_notes", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.NoteType).HasColumnName("note_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Visibility).HasColumnName("visibility").HasMaxLength(20).IsRequired();
        b.Property(e => e.AuthorType).HasColumnName("author_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.AuthorId).HasColumnName("author_id");
        b.Property(e => e.AuthorName).HasColumnName("author_name").HasMaxLength(200);
        b.Property(e => e.NoteText).HasColumnName("note_text").IsRequired();
        b.Property(e => e.Attachments).HasColumnName("attachments").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.IsPinned).HasColumnName("is_pinned").IsRequired();
        b.Property(e => e.IsResolved).HasColumnName("is_resolved").IsRequired();
        b.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        b.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Composite FK to partitioned orders table
        b.HasOne(e => e.Order)
            .WithMany(o => o.Notes)
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("order_notes_order_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("order_notes_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
