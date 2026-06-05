using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// Customer mobile auth endpoints under /api/v1/customer/auth.
/// Brand resolution order: X-Brand-Id header → brandCode body field → DefaultBrandCode config.
/// All endpoints carry the "auth" rate-limiting policy.
/// GET /me is CustomerOnly — requires token_use=customer.
/// </summary>
public static class CustomerAuthEndpoints
{
    public static RouteGroupBuilder MapCustomerAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/customer/auth")
            .WithTags("Customer Auth")
            .RequireRateLimiting("auth");

        // POST /api/v1/customer/auth/otp/send
        auth.MapPost("/otp/send", async (
            CustomerOtpSendRequest req,
            HttpContext ctx,
            ISender sender,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var brandId = await ResolveBrandIdAsync(ctx, req.BrandCode, config, sender, ct);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new CustomerOtpSendCommand(req.Phone, brandId, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .WithName("CustomerOtpSend")
        .AllowAnonymous();

        // POST /api/v1/customer/auth/otp/verify
        auth.MapPost("/otp/verify", async (
            CustomerOtpVerifyRequest req,
            HttpContext ctx,
            ISender sender,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var brandId = await ResolveBrandIdAsync(ctx, req.BrandCode, config, sender, ct);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new CustomerOtpVerifyCommand(req.Phone, req.Code, brandId, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .WithName("CustomerOtpVerify")
        .AllowAnonymous();

        // POST /api/v1/customer/auth/refresh
        auth.MapPost("/refresh", async (
            RefreshTokenRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new CustomerRefreshCommand(req.RefreshToken, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .WithName("CustomerRefresh")
        .AllowAnonymous();

        // POST /api/v1/customer/auth/logout
        auth.MapPost("/logout", async (
            LogoutRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new CustomerLogoutCommand(req.RefreshToken), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Logged out." } });
        })
        .WithName("CustomerLogout")
        .RequireAuthorization("CustomerOnly");

        // GET /api/v1/customer/auth/me — CustomerOnly: verifies system tokens are rejected
        auth.MapGet("/me", async (
            HttpContext ctx,
            LaundryGharDbContext db,
            CancellationToken ct) =>
        {
            // JwtBearerMiddleware maps "sub" → ClaimTypes.NameIdentifier
            var subClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? ctx.User.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var customerId))
                return Results.Unauthorized();

            var c = await db.Customers.AsNoTracking()
                .Where(x => x.Id == customerId)
                .Select(x => new CustomerMeResponse(
                    x.Id, x.BrandId, x.PhoneE164,
                    x.FirstName, x.LastName, x.DisplayName, x.Status))
                .FirstOrDefaultAsync(ct);

            return c is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<CustomerMeResponse> { Status = true, Data = c });
        })
        .WithName("CustomerMe")
        .RequireAuthorization("CustomerOnly");

        return group;
    }

    /// <summary>
    /// Resolves the brand to use for customer auth.
    /// Priority: X-Brand-Id header (Guid) → brandCode (string lookup) → CustomerAuth:DefaultBrandCode config.
    /// Throws ValidationException if the resolved brand does not exist.
    /// </summary>
    private static async Task<Guid> ResolveBrandIdAsync(
        HttpContext ctx,
        string? bodyBrandCode,
        IConfiguration config,
        ISender sender,
        CancellationToken ct)
    {
        using var scope = ctx.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        // 1. X-Brand-Id header (direct Guid)
        if (ctx.Request.Headers.TryGetValue("X-Brand-Id", out var headerVal)
            && Guid.TryParse(headerVal, out var headerId))
        {
            var exists = await db.Brands.AnyAsync(b => b.Id == headerId, ct);
            if (!exists)
                throw new laundryghar.Utilities.Exceptions.ValidationException(
                    new Dictionary<string, string[]> { ["brandId"] = ["Brand not found."] });
            return headerId;
        }

        // 2. brandCode from request body
        var codeToResolve = bodyBrandCode
            ?? config["CustomerAuth:DefaultBrandCode"]
            ?? "LG-MAIN";

        var brand = await db.Brands
            .Where(b => b.Code == codeToResolve)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(ct);

        if (brand is null)
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["brandCode"] = [$"Brand '{codeToResolve}' not found."] });

        return brand.Id;
    }
}
