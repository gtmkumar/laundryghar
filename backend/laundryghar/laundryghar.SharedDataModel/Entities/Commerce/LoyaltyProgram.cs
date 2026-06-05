using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Brand loyalty programme configuration (commerce.loyalty_programs).
/// Has created_at, updated_at, created_by, updated_by — NO version, NO deleted_at.
/// One programme per brand (brand_id is UNIQUE).</summary>
public class LoyaltyProgram
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public decimal EarnRate { get; set; }
    public string EarnBasis { get; set; } = null!;
    public decimal BurnRate { get; set; }
    public int MinBurnPoints { get; set; }
    public decimal MaxBurnPerOrderPct { get; set; }
    public decimal MinOrderForEarn { get; set; }

    /// <summary>uuid[] — service IDs excluded from earning.</summary>
    public Guid[] ExcludedServices { get; set; } = [];

    public short? PointExpiryMonths { get; set; }
    public int WelcomeBonus { get; set; }
    public int ReferralBonusReferrer { get; set; }
    public int ReferralBonusReferee { get; set; }
    public int BirthdayBonus { get; set; }

    /// <summary>jsonb — tier config map.</summary>
    public string TierConfig { get; set; } = null!;

    public string? Terms { get; set; }
    public DateTimeOffset? LaunchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<LoyaltyPointsLedger> PointsLedgerEntries { get; set; } = [];
}
