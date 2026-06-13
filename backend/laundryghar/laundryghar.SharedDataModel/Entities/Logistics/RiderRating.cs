namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Customer → rider rating with attribution (logistics.rider_ratings), separate
/// from the order-level rating. Maintains the rider's aggregate rating_average/count.</summary>
public class RiderRating
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string? LegType { get; set; }
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public bool IsFlagged { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public Rider Rider { get; set; } = null!;
}
