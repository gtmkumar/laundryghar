using System.Security.Claims;

namespace laundryghar.Utilities.Auth;

/// <summary>
/// Reads the <c>ent_off</c> claim — the FEATURE keys a brand has not licensed
/// (PLATFORM_STRATEGY.md §5) — so an authorization denial caused by the PLAN can be told apart from
/// one caused by PERMISSIONS.
///
/// <para>The distinction is not cosmetic. The entitlement filter strips un-entitled permissions from
/// the token at mint, so by the time a request is denied both cases look identical: the permission
/// simply is not there. Answering 403 for both tells a paying customer they lack permission when what
/// they actually lack is the plan — and gives them no way to fix it. 402 names the feature and points
/// at the upgrade.</para>
/// </summary>
public static class FeatureNotInPlan
{
    /// <summary>The response code clients switch on.</summary>
    public const string ResponseMessage = "feature_not_in_plan";

    /// <summary>The features this principal's brand has NOT licensed. Empty when the claim is absent,
    /// which is the case whenever enforcement is off or the brand owns everything.</summary>
    public static IReadOnlyCollection<string> UnlicensedFeatures(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(TokenClaims.EntitlementOffClaim);
        if (string.IsNullOrEmpty(raw)) return [];
        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>True if <paramref name="featureKey"/> is one the brand has not licensed.</summary>
    public static bool IsUnlicensed(ClaimsPrincipal user, string? featureKey)
        => featureKey is not null
           && UnlicensedFeatures(user).Contains(featureKey, StringComparer.OrdinalIgnoreCase);
}
