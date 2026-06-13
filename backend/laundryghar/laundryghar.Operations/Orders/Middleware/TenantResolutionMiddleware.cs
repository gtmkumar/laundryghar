namespace laundryghar.Orders.Middleware;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userType = context.User.FindFirstValue("user_type");
            if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
            {
                context.Items["bypass_rls"] = true;
                if (context.Request.Headers.TryGetValue("X-Brand-Id", out var h)
                    && Guid.TryParse(h, out var b))
                    context.Items["brand_id_override"] = b;
            }
        }
        await _next(context);
    }
}
