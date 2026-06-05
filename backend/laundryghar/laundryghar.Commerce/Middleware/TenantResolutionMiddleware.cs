namespace laundryghar.Commerce.Middleware;

/// <summary>
/// Runs after authentication. Reads brand_id/franchise_id/store_id from JWT claims
/// and applies X-Brand-Id override for platform admins.
/// Sets HttpContext.Items["bypass_rls"] = true for platform admins so ICurrentTenant.BypassRls works.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userType = context.User.FindFirstValue("user_type");
            bool isPlatformAdmin = userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin;

            if (isPlatformAdmin)
            {
                context.Items["bypass_rls"] = true;

                if (context.Request.Headers.TryGetValue("X-Brand-Id", out var brandHeader)
                    && Guid.TryParse(brandHeader, out var brandOverride))
                {
                    context.Items["brand_id_override"] = brandOverride;
                }
            }
        }

        await _next(context);
    }
}
