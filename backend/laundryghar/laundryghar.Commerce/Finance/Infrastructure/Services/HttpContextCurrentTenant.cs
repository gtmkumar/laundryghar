using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Finance.Infrastructure.Services;

/// <summary>
/// Resolves RLS tenant from JWT claims. Finance is admin-only; postgres superuser
/// bypasses DB-level RLS — the BrandId predicate in every handler is the real guard.
/// </summary>
public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _a;
    public HttpContextCurrentTenant(IHttpContextAccessor a) => _a = a;

    public Guid? BrandId
    {
        get
        {
            var ctx = _a.HttpContext;
            if (ctx is null) return null;

            // Platform admin with brand override
            if (ctx.Items.TryGetValue("brand_id_override", out var v) && v is Guid g && g != Guid.Empty)
                return g;

            // JWT brand_id
            var raw = ctx.User.FindFirstValue("brand_id");
            return Guid.TryParse(raw, out var b) ? b : null;
        }
    }

    public Guid? FranchiseId
    {
        get
        {
            var raw = _a.HttpContext?.User.FindFirstValue("franchise_id");
            return Guid.TryParse(raw, out var f) ? f : null;
        }
    }

    public Guid? StoreId
    {
        get
        {
            var raw = _a.HttpContext?.User.FindFirstValue("store_id");
            return Guid.TryParse(raw, out var s) ? s : null;
        }
    }

    public Guid? UserId
    {
        get
        {
            var raw = _a.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var u) ? u : null;
        }
    }

    public bool BypassRls =>
        _a.HttpContext?.Items.ContainsKey("bypass_rls") == true;
}
