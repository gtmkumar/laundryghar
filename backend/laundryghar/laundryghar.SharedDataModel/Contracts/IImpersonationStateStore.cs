namespace laundryghar.SharedDataModel.Contracts;

/// <summary>Live state of one impersonation grant, read fresh on every request.</summary>
/// <param name="Status">pending | approved | denied | revoked | expired.</param>
/// <param name="Scope">read_only | read_write.</param>
public sealed record ImpersonationState(
    string Status, string Scope, Guid BrandId, Guid SupportUserId, DateTimeOffset? ExpiresAt);

/// <summary>
/// Resolves an impersonation grant's CURRENT state.
///
/// <para>Deliberately uncached, unlike <see cref="IBrandStatusStore"/>. A cache TTL there is the
/// delay before a suspension bites; a cache TTL here would be the window in which a provider has
/// pressed "end this session" and support is still inside their account. Revocation that takes
/// effect "within 30 seconds" is not revocation, so this pays a lookup per impersonated request —
/// a cost borne only by the rare support session, never by ordinary traffic.</para>
/// </summary>
public interface IImpersonationStateStore
{
    Task<ImpersonationState?> GetAsync(Guid grantId, CancellationToken ct = default);

    /// <summary>
    /// Records a support engineer's request for access to <paramref name="brandId"/> and returns the
    /// new (or existing live) grant id. Null means the brand does not exist.
    ///
    /// <para>Lives here rather than in a handler because it is the second half of the same boundary:
    /// a support caller has no brand and no RLS bypass, so both the read and this write go through
    /// single-capability SECURITY DEFINER functions instead of a blanket bypass. The row it creates
    /// is always <c>pending</c>, which authorises nothing.</para>
    /// </summary>
    Task<Guid?> RequestAsync(
        Guid brandId, Guid supportUserId, string reason, string scope, CancellationToken ct = default);
}
