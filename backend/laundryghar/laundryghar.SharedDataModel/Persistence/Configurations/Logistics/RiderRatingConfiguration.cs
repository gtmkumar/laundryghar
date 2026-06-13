using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderRatingConfiguration : IEntityTypeConfiguration<RiderRating>
{
    public void Configure(EntityTypeBuilder<RiderRating> b)
    {
        b.ToTable("rider_ratings", "logistics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.LegType).HasColumnName("leg_type").HasMaxLength(20);
        b.Property(e => e.Rating).HasColumnName("rating").IsRequired();
        b.Property(e => e.Comment).HasColumnName("comment");
        b.Property(e => e.IsFlagged).HasColumnName("is_flagged").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Rider).WithMany().HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_ratings_rider_id_fkey");
    }
}
