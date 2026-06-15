using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Services;

/// <summary>
/// Resolves a brand ID from an HTTP request without requiring authentication.
/// Used by anonymous public endpoints (onboarding, banners, app-config) that
/// must be brand-scoped even before the customer logs in.
///
/// Resolution order:
/// 1. X-Brand-Id header (UUID) — direct resolution, no DB lookup.
/// 2. ?brandCode= query parameter — resolves via brands table lookup (cached for the request).
/// 3. Default brand code "LG-MAIN" — falls back to the platform's primary brand.
///
/// NOTE: Because these endpoints are anonymous, RLS cannot be used to filter rows.
/// All anonymous queries MUST add an explicit .Where(x => x.BrandId == resolvedBrandId) predicate.
/// </summary>
public interface IBrandResolver
{
    Task<Guid?> ResolveAsync(HttpContext context, CancellationToken ct = default);
}
