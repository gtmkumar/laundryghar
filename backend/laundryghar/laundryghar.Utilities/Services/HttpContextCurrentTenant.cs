using System.Security.Claims;
using laundryghar.SharedDataModel.Contracts;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Services;

/// <summary>
/// <see cref="ICurrentTenant"/> backed by HttpContext JWT claims, consumed by the shared
/// RLS connection interceptor. Populated by TenantResolutionMiddleware after authentication;
/// platform admins may set <c>BypassRls=true</c> via <c>HttpContext.Items["bypass_rls"]</c>.
///
/// Cross-cutting: shared by every bounded-context host (Core, Operations, …) — one tenant
/// adapter serves the whole service. Register via <c>services.AddCurrentTenant()</c>.
/// </summary>
public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentTenant(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? BrandId     => GetGuid("brand_id");
    public Guid? FranchiseId => GetGuid("franchise_id");
    public Guid? StoreId     => GetGuid("store_id");
    public Guid? PartnerId   => GetGuid("partner_id");
    public bool  BypassRls   => _accessor.HttpContext?.Items["bypass_rls"] is true;

    // A0.6 — `sub` carries the STAFF user id on a token_use=user JWT and the CUSTOMER id on a
    // token_use=customer one (TokenClaims.cs:87 pins that contract). Reading it unconditionally
    // meant app.current_user_id was set to a customer's id on every customer request, while
    // app.current_customer_id — which ten rls_brand_or_customer policies read — was never set.
    //
    // The unset GUC made those ten policies FAIL OPEN, not closed. Their qual is
    //   rls_bypass() OR (brand_id = current_brand_id()
    //                    AND (current_customer_id() IS NULL OR customer_id = current_customer_id()))
    // so with the GUC unset the `IS NULL` arm short-circuits to TRUE and each policy degrades to
    // plain brand equality: every customer in a brand could see every other customer's payments,
    // wallets, refunds, loyalty and notification preferences as far as RLS was concerned. Customer
    // isolation rested entirely on per-handler ownership checks. Setting the GUC is what closes it.
    //
    // UserId excludes ONLY customer tokens, so riders (token_use=user), partners and API keys keep
    // exactly the behaviour they had; a customer's id simply stops being published as a user id.
    public Guid? UserId     => IsCustomerToken ? null : GetGuid(ClaimTypes.NameIdentifier);
    public Guid? CustomerId => IsCustomerToken ? GetGuid(ClaimTypes.NameIdentifier) : null;

    // A5.2 — the subject attributes authz.permits() reads from session GUCs. Published as raw
    // claim strings: the space-separated form is the JWT's own, so there is one format across the
    // token, the C# resolver and the SQL evaluator rather than three.
    public string? UserType    => Claim("user_type");
    public string? TokenUse    => Claim("token_use");
    public string? ScopeNodes  => Claim("scope_nodes");
    public string? Roles       => Claim(Auth.TokenClaims.RolesClaim);

    // Coalesced to empty, unlike the two above. A token that carries no `permissions` claim — a
    // customer or partner token — genuinely HAS no admin permissions, and that is a known fact, not
    // an unknown one. scope_nodes and roles stay null when absent because there "absent" really does
    // mean unresolved (a pre-feature token), and conflating the two would let an old token satisfy
    // a scope rule it was never evaluated against.
    public string? Permissions => Claim("permissions") ?? string.Empty;

    private string? Claim(string claimType) => _accessor.HttpContext?.User?.FindFirstValue(claimType);

    private bool IsCustomerToken
    {
        get
        {
            var tokenUse = _accessor.HttpContext?.User?.FindFirstValue("token_use");
            return string.Equals(tokenUse, Auth.CustomerTokenClaims.TokenUseValue, StringComparison.Ordinal)
                || string.Equals(tokenUse, Auth.CustomerTokenClaims.OAuthTokenUseValue, StringComparison.Ordinal);
        }
    }

    private Guid? GetGuid(string claimType)
    {
        var value = _accessor.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var g) ? g : null;
    }
}
