using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Configurable payment method for a brand (commerce.payment_methods).
/// Has created_at, updated_at, created_by, updated_by — NO version, NO deleted_at.</summary>
public class PaymentMethod
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>jsonb — localised name map.</summary>
    public string NameLocalized { get; set; } = null!;

    public string MethodType { get; set; } = null!;
    public string? Gateway { get; set; }
    public string? IconUrl { get; set; }
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public string? ConvenienceFeeType { get; set; }
    public decimal? ConvenienceFeeValue { get; set; }
    public bool IsOnline { get; set; }
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; }
    public short DisplayOrder { get; set; }

    /// <summary>jsonb — gateway / provider configuration.</summary>
    public string Config { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = [];
}
