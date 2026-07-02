using System.Globalization;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Common;

/// <summary>
/// Resolves the price of a value-slab (branded/luxury) garment from its declared value, and
/// guards slab authoring against overlapping ranges (GH #22).
///
/// <para><b>Resolution</b> (<see cref="ResolveSlabPriceAsync"/>): the service-specific lane is tried
/// first, then the brand-wide (null-service) lane. Within a lane the matching slab is the one whose
/// range <c>[MinValue, MaxValue)</c> contains the declared value — min inclusive, max exclusive, a
/// null MaxValue being an open-ended top slab.</para>
///
/// <para><b>Lanes</b>: service-specific slabs and brand-wide (null-service) slabs are SEPARATE lanes.
/// They may overlap each other by design (a service-specific slab wins over the brand-wide one for
/// that service); each lane is kept internally non-overlapping by
/// <see cref="EnsureNoOverlapAsync"/> on create/update.</para>
/// </summary>
public static class ValueSlabResolver
{
    /// <summary>Raised when a value-slab item is ordered without a positive declared value.</summary>
    public const string DeclaredValueRequiredCode = "declared_value_required";

    /// <summary>Raised when no active slab covers the declared value in either lane.</summary>
    public const string NoSlabMatchCode = "no_value_slab_match";

    /// <summary>Raised when a create/update would overlap an existing active slab in the same lane.</summary>
    public const string OverlapCode = "value_slab_overlap";

    /// <summary>Throws <see cref="StructuredBusinessRuleException"/> (declared_value_required) when a
    /// value-slab garment is ordered without a positive declared value.</summary>
    public static void RequireDeclaredValue(decimal? declaredValue, Guid itemId, string itemName)
    {
        if (declaredValue is > 0m) return;
        throw new StructuredBusinessRuleException(
            DeclaredValueRequiredCode,
            $"“{itemName}” is priced by declared value — enter the garment's value to price this item.",
            new Dictionary<string, string>
            {
                ["itemId"]   = itemId.ToString(),
                ["itemName"] = itemName,
            });
    }

    /// <summary>Resolves the slab price for a declared value: service-specific lane first, then the
    /// brand-wide (null-service) lane. Throws <see cref="StructuredBusinessRuleException"/>
    /// (no_value_slab_match) when neither lane covers the value.</summary>
    public static async Task<decimal> ResolveSlabPriceAsync(
        IOperationsDbContext db, Guid brandId, Guid serviceId, decimal declaredValue, Guid itemId,
        CancellationToken ct)
    {
        // Load both lanes for this brand in one round-trip, then pick in memory.
        var candidates = await db.ValuePriceSlabs.AsNoTracking()
            .Where(s => s.BrandId == brandId
                     && s.Status == "active"
                     && (s.ServiceId == serviceId || s.ServiceId == null)
                     && s.MinValue <= declaredValue
                     && (s.MaxValue == null || declaredValue < s.MaxValue))
            .Select(s => new { s.ServiceId, s.Price })
            .ToListAsync(ct);

        // Service-specific lane wins; fall back to the brand-wide (null-service) lane.
        var match = candidates.FirstOrDefault(c => c.ServiceId == serviceId)
                 ?? candidates.FirstOrDefault(c => c.ServiceId == null);

        if (match is null)
            throw new StructuredBusinessRuleException(
                NoSlabMatchCode,
                $"No value slab is configured for a declared value of " +
                $"{declaredValue.ToString("0.##", CultureInfo.InvariantCulture)}.",
                new Dictionary<string, string>
                {
                    ["itemId"]        = itemId.ToString(),
                    ["declaredValue"] = declaredValue.ToString(CultureInfo.InvariantCulture),
                });

        return match.Price;
    }

    /// <summary>Guards that <c>[minValue, maxValue)</c> does not overlap any other active slab in the
    /// SAME lane (same brand + same service_id, null matching null). Excludes <paramref name="excludeId"/>
    /// (the row being updated). Throws <see cref="StructuredBusinessRuleException"/> (value_slab_overlap)
    /// on the first conflict.</summary>
    public static async Task EnsureNoOverlapAsync(
        IOperationsDbContext db, Guid brandId, Guid? serviceId,
        decimal minValue, decimal? maxValue, Guid? excludeId, CancellationToken ct)
    {
        var lane = await db.ValuePriceSlabs.AsNoTracking()
            .Where(s => s.BrandId == brandId
                     && s.Status == "active"
                     && s.ServiceId == serviceId
                     && (excludeId == null || s.Id != excludeId))
            .Select(s => new { s.Id, s.MinValue, s.MaxValue })
            .ToListAsync(ct);

        // Half-open ranges [a,b) and [c,d) overlap iff a < d && c < b, with null max = +inf.
        var newMax = maxValue ?? decimal.MaxValue;
        foreach (var s in lane)
        {
            var existMax = s.MaxValue ?? decimal.MaxValue;
            if (minValue < existMax && s.MinValue < newMax)
                throw new StructuredBusinessRuleException(
                    OverlapCode,
                    "This value range overlaps an existing slab. Adjust the bounds so ranges don't overlap.",
                    new Dictionary<string, string>
                    {
                        ["conflictSlabId"] = s.Id.ToString(),
                        ["conflictMin"]    = s.MinValue.ToString(CultureInfo.InvariantCulture),
                        ["conflictMax"]    = s.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "",
                    });
        }
    }
}
