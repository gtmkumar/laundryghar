using core.Application.Common.Interfaces;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace core.Application.Identity.Auth.Common;

/// <summary>
/// Resolves the brand for a customer-auth request.
/// Priority: X-Brand-Id header (Guid) → body brandCode (string lookup)
///           → CustomerAuth:DefaultBrandCode config → "LG-MAIN".
/// Throws <see cref="ValidationException"/> if the resolved brand does not exist.
///
/// Ported from the legacy endpoint's ResolveBrandIdAsync — the resolution now lives in the
/// command handlers (thin-endpoint convention) rather than the endpoint, but the priority
/// order and throw behavior are preserved exactly.
/// </summary>
internal static class CustomerBrandResolver
{
    public static async Task<Guid> ResolveAsync(
        ICoreDbContext db,
        IConfiguration config,
        Guid? headerBrandId,
        string? bodyBrandCode,
        CancellationToken ct)
    {
        // 1. X-Brand-Id header (direct Guid)
        if (headerBrandId.HasValue)
        {
            var headerId = headerBrandId.Value;
            var exists = await db.Brands.AnyAsync(b => b.Id == headerId, ct);
            if (!exists)
                throw new ValidationException(
                    new Dictionary<string, string[]> { ["brandId"] = ["Brand not found."] });
            return headerId;
        }

        // 2. brandCode from request body → config default → hardcoded fallback
        var codeToResolve = bodyBrandCode
            ?? config["CustomerAuth:DefaultBrandCode"]
            ?? "LG-MAIN";

        var brand = await db.Brands
            .Where(b => b.Code == codeToResolve)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(ct);

        if (brand is null)
            throw new ValidationException(
                new Dictionary<string, string[]> { ["brandCode"] = [$"Brand '{codeToResolve}' not found."] });

        return brand.Id;
    }
}
