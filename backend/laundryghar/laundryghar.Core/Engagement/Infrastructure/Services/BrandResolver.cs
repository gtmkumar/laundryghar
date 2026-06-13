namespace laundryghar.Engagement.Infrastructure.Services;

/// <summary>
/// Resolves brand ID for anonymous public endpoints.
///
/// Design note on anonymous brand resolution:
///   RLS policies are PostgreSQL row-level security which is driven by the SET LOCAL app.current_brand_id
///   session variable — that variable is set by the RLS interceptor only when an authenticated token
///   is present.  For anonymous requests there is no token, so RLS is never activated and every query
///   would see ALL brands.  We must therefore always add an explicit brand predicate in the LINQ query
///   for public endpoints.  This resolver provides the brandId to pass into those predicates.
/// </summary>
public sealed class BrandResolver : IBrandResolver
{
    private const string DefaultBrandCode = "LG-MAIN";

    private readonly LaundryGharDbContext _db;
    private readonly ILogger<BrandResolver> _logger;

    public BrandResolver(LaundryGharDbContext db, ILogger<BrandResolver> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<Guid?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        // 1. X-Brand-Id header (UUID) — fastest path; no DB lookup needed.
        if (context.Request.Headers.TryGetValue("X-Brand-Id", out var headerVal)
            && Guid.TryParse(headerVal, out var headerGuid))
        {
            return headerGuid;
        }

        // 2. ?brandCode= query parameter — resolve via DB lookup.
        var brandCode = context.Request.Query["brandCode"].FirstOrDefault()
                     ?? DefaultBrandCode;

        var brand = await _db.Brands.IgnoreQueryFilters()
            .Where(b => b.Code == brandCode && b.DeletedAt == null)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(ct);

        if (brand is null)
        {
            _logger.LogWarning("BrandResolver: brand code '{Code}' not found.", brandCode);
            return null;
        }

        return brand.Id;
    }
}
