namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>A rider-initiated withdrawal of earned payout (logistics.rider_payout_requests).
/// requested → approved/rejected → paid. Paying posts a cash_out entry to the cash book.</summary>
public class RiderPayoutRequest
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>requested|approved|rejected|paid.</summary>
    public string Status { get; set; } = "requested";
    public string? RejectionReason { get; set; }
    public string? PaymentReference { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? PaidBy { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Rider Rider { get; set; } = null!;
}
