using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Declared-value pricing slab for branded/luxury garments
/// (customer_catalog.value_price_slabs, GH #22).
///
/// <para>A slab maps a declared-garment-value range <c>[MinValue, MaxValue)</c> to a flat
/// <see cref="Price"/>. <see cref="MaxValue"/> null = open-ended top slab. <see cref="ServiceId"/>
/// null = brand-wide lane (any service); a non-null value scopes the slab to one service, and
/// service-specific slabs win over the brand-wide lane at resolution.</para>
///
/// <para>Brand-scoped (RLS). Has created_at, updated_at, created_by, updated_by, version. No
/// deleted_at — retirement is a soft <see cref="Status"/> move ('inactive'/'archived').</para></summary>
public class ValuePriceSlab : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>Null = brand-wide lane (applies to any service). Non-null = service-scoped slab
    /// (wins over the brand-wide lane when both match a declared value).</summary>
    public Guid? ServiceId { get; set; }

    /// <summary>Inclusive lower bound of the declared-value range.</summary>
    public decimal MinValue { get; set; }

    /// <summary>Exclusive upper bound; null = open-ended top slab.</summary>
    public decimal? MaxValue { get; set; }

    /// <summary>Flat price charged when a garment's declared value lands in this slab.</summary>
    public decimal Price { get; set; }

    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Service? Service { get; set; }
}
