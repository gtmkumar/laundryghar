using laundryghar.SharedDataModel.Common;

namespace commerce.Application.Common.Interfaces;

/// <summary>
/// Per-brand, TTL-cached resolver for <see cref="PaymentGatewaySettings"/> stored in
/// <c>kernel.system_settings</c>. Abstracted here in the Application layer so the Razorpay
/// webhook handler can resolve a brand's webhook secret without depending on
/// commerce.Infrastructure (where the concrete <c>GatewaySettingsCache</c> lives).
///
/// SECURITY (SEC-2): the cache is keyed by <c>brandId</c>; a brand's decrypted credentials are
/// never served to another brand. The webhook handler resolves the matched payment FIRST, then
/// fetches the secret scoped to <em>that</em> payment's brand.
/// </summary>
public interface IGatewaySettingsCache
{
    /// <summary>
    /// Returns a live (non-stale) <see cref="PaymentGatewaySettings"/> for the given
    /// <paramref name="brandId"/> (<c>null</c> resolves the global config row). Refreshes that
    /// brand's slot from the database when expired.
    /// </summary>
    Task<PaymentGatewaySettings> GetAsync(ICommerceDbContext db, Guid? brandId, CancellationToken ct);

    /// <summary>Forces expiry of every cached brand slot so the next call re-fetches from DB.</summary>
    void Invalidate();

    /// <summary>Forces expiry of a single brand's slot.</summary>
    void Invalidate(Guid? brandId);
}
