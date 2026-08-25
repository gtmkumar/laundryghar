namespace laundryghar.SharedDataModel.Contracts;

/// <summary>
/// Answers "which FEATURE must a brand own for this permission to be usable?" — the
/// permission → module → feature hop introduced by migration 0005.
///
/// <para>Needed because the authorization result handler has to tell two denials apart that look
/// identical from the token alone: a permission the caller was never granted (403) versus one the
/// entitlement filter stripped because the brand has not bought the feature (402). The token carries
/// the un-licensed feature keys (<c>ent_off</c>), but not the permission→feature map — that map is
/// global, not per-brand, so shipping it in every JWT would be waste. It is looked up here instead,
/// behind a cache.</para>
///
/// <para>Lives in Contracts (alongside <see cref="ITokenVersionStore"/>) because the consumer is in
/// laundryghar.Utilities, which references SharedDataModel and not the other way round.</para>
/// </summary>
public interface IFeatureCatalog
{
    /// <summary>
    /// The feature gating <paramref name="permissionCode"/>, or <c>null</c> when the permission is
    /// not gated by any feature — an orphan permission (no owning module), a core module, or an
    /// unknown code. Null must be read as "entitlement has nothing to say about this", never as
    /// "not entitled".
    /// </summary>
    Task<string?> FeatureForPermissionAsync(string permissionCode, CancellationToken ct = default);
}
