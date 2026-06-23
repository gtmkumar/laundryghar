using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class GarmentInspectionPhotoConfiguration : IEntityTypeConfiguration<GarmentInspectionPhoto>
{
    public void Configure(EntityTypeBuilder<GarmentInspectionPhoto> b)
    {
        b.ToTable("garment_inspection_photos", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.InspectionId).HasColumnName("inspection_id").IsRequired();
        b.Property(e => e.GarmentId).HasColumnName("garment_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.S3Key).HasColumnName("s3_key").IsRequired();
        b.Property(e => e.ThumbnailS3Key).HasColumnName("thumbnail_s3_key");
        b.Property(e => e.CdnUrl).HasColumnName("cdn_url");
        b.Property(e => e.View).HasColumnName("view").HasMaxLength(20).IsRequired();
        b.Property(e => e.Annotations).HasColumnName("annotations").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.WidthPx).HasColumnName("width_px");
        b.Property(e => e.HeightPx).HasColumnName("height_px");
        b.Property(e => e.Bytes).HasColumnName("bytes");
        b.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(50).IsRequired();
        b.Property(e => e.IsCompressed).HasColumnName("is_compressed").IsRequired();
        b.Property(e => e.HasExif).HasColumnName("has_exif").IsRequired();
        b.Property(e => e.ExifData).HasColumnName("exif_data").HasColumnType("jsonb");
        b.Property(e => e.CapturedAt).HasColumnName("captured_at").IsRequired();
        b.Property(e => e.CapturedBy).HasColumnName("captured_by");
        b.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
        b.Property(e => e.IsPrimary).HasColumnName("is_primary").IsRequired();
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Inspection).WithMany(i => i.Photos)
            .HasForeignKey(e => e.InspectionId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("garment_inspection_photos_inspection_id_fkey");
        b.HasOne(e => e.Garment).WithMany(g => g.InspectionPhotos)
            .HasForeignKey(e => e.GarmentId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("garment_inspection_photos_garment_id_fkey");
        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garment_inspection_photos_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
