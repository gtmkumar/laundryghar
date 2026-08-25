namespace laundryghar.SharedDataModel.Contracts;

/// <summary>
/// The lifecycle status of a brand (<c>active</c> / <c>suspended</c> / <c>archived</c>), read on the
/// request path to enforce §9's "suspension = login-only mode".
///
/// <para>Deliberately NOT a token claim. Suspension has to bite the moment a payment lapses, and a
/// claim would only take effect at the next login — a brand could keep operating for the life of its
/// access token. So it is a cached lookup instead, with a short TTL bounding how long a suspension
/// (or a reinstatement) takes to apply. Same trade-off as <see cref="ITokenVersionStore"/>.</para>
/// </summary>
public interface IBrandStatusStore
{
    /// <summary>The brand's status, or <c>null</c> if unknown/not found. Callers must treat null as
    /// "do not block" — never fail a request because a status lookup came back empty.</summary>
    Task<string?> GetStatusAsync(Guid brandId, CancellationToken ct = default);

    /// <summary>
    /// Moves a brand into or out of the §9 wind-down and returns its PREVIOUS status (null if the
    /// brand does not exist).
    ///
    /// <para>Goes through <c>kernel.set_brand_cancellation_state</c>, which accepts only
    /// <c>active</c> and <c>cancelled</c>. An owner cancelling their own account is ordinary tenant
    /// traffic, and <c>tenancy_org.brands</c> is admin-only under RLS — an EF write here silently
    /// affects zero rows and an EF read answers "your brand does not exist" to the person who owns
    /// it. The narrowness is the safety: this cannot suspend (billing's decision) and cannot archive
    /// (the purge's, and only after a window elapses).</para>
    ///
    /// <para>Evicts this store's cache entry, so the suspension gate sees the change on the next
    /// request rather than after the TTL.</para>
    /// </summary>
    Task<string?> SetCancellationStateAsync(Guid brandId, string status, CancellationToken ct = default);
}
