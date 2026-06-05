using laundryghar.Commerce.Application;
using laundryghar.Commerce.Application.Customer.Coupons;
using laundryghar.Commerce.Application.Customer.Loyalty;
using laundryghar.Commerce.Application.Customer.Packages;
using laundryghar.Commerce.Application.Customer.Payments;
using laundryghar.Commerce.Application.Customer.Wallet;
using MediatR;

namespace laundryghar.Commerce.Endpoints;

/// <summary>
/// Customer-facing commerce endpoints — require CustomerOnly policy (token_use=customer).
/// All queries self-filtered by sub claim (customerId) and brand_id claim.
/// </summary>
public static class CustomerCommerceEndpoints
{
    public static RouteGroupBuilder MapCustomerCommerceEndpoints(this RouteGroupBuilder group)
    {
        // ── Packages ──────────────────────────────────────────────────────────
        var pkgs = group.MapGroup("/packages").WithTags("Customer - Packages");

        pkgs.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetAvailablePackagesQuery(customerId, brandId), ct);
            return Results.Ok(new ListResponse<PackageDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        pkgs.MapGet("/my", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyPackagesQuery(customerId, brandId), ct);
            return Results.Ok(new ListResponse<CustomerPackageDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        pkgs.MapGet("/my/{id:guid}/usage", async (HttpContext http, Guid id, ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyPackageUsageQuery(customerId, id, brandId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PackageUsageLedgerDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        pkgs.MapPost("/purchase/initiate", async (HttpContext http, PurchasePackageRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var idempotencyKey = GetIdempotencyKey(http) ?? $"pkg_{customerId}_{req.PackageId}_{Guid.NewGuid():N}";
            var r = await sender.Send(new PurchasePackageInitiateCommand(customerId, brandId, req, idempotencyKey), ct);
            return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        pkgs.MapPost("/purchase/verify", async (HttpContext http, VerifyPaymentRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new PurchasePackageVerifyCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<CustomerPackageDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Loyalty ───────────────────────────────────────────────────────────
        var loyalty = group.MapGroup("/loyalty").WithTags("Customer - Loyalty");

        loyalty.MapGet("/balance", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyLoyaltyBalanceQuery(customerId, brandId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyBalanceDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Coupons ───────────────────────────────────────────────────────────
        var coupons = group.MapGroup("/coupons").WithTags("Customer - Coupons");

        coupons.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetApplicableCouponsQuery(customerId, brandId), ct);
            return Results.Ok(new ListResponse<CouponDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        coupons.MapPost("/validate-apply", async (HttpContext http, ValidateCouponRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new ValidateApplyCouponCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<CouponRedemptionDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Wallet ────────────────────────────────────────────────────────────
        var wallet = group.MapGroup("/wallet").WithTags("Customer - Wallet");

        wallet.MapGet("/", async (HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyWalletQuery(customerId, brandId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WalletAccountDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        wallet.MapGet("/transactions", async (HttpContext http, ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new GetMyWalletTransactionsQuery(customerId, brandId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<WalletTransactionDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        wallet.MapPost("/topup/initiate", async (HttpContext http, WalletTopUpRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var idempotencyKey = GetIdempotencyKey(http) ?? $"topup_{customerId}_{Guid.NewGuid():N}";
            var r = await sender.Send(new WalletTopUpInitiateCommand(customerId, brandId, req, idempotencyKey), ct);
            return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        wallet.MapPost("/topup/verify", async (HttpContext http, VerifyPaymentRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new WalletTopUpVerifyCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<WalletTransactionDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        // ── Payments (shared initiate/verify for any purpose) ─────────────────
        var payments = group.MapGroup("/payments").WithTags("Customer - Payments");

        payments.MapPost("/initiate", async (HttpContext http, InitiatePaymentRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var idempotencyKey = GetIdempotencyKey(http) ?? $"pay_{customerId}_{Guid.NewGuid():N}";
            var r = await sender.Send(new InitiatePaymentCommand(customerId, brandId, req, idempotencyKey), ct);
            return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        payments.MapPost("/verify", async (HttpContext http, VerifyPaymentRequest req, ISender sender, CancellationToken ct) =>
        {
            var (customerId, brandId) = GetIds(http);
            if (customerId == Guid.Empty) return Results.Unauthorized();
            var r = await sender.Send(new VerifyPaymentCommand(customerId, brandId, req), ct);
            return Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("CustomerOnly");

        return group;
    }

    private static (Guid CustomerId, Guid BrandId) GetIds(HttpContext http)
    {
        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var brd = http.User.FindFirstValue("brand_id");
        Guid.TryParse(sub, out var customerId);
        Guid.TryParse(brd, out var brandId);
        return (customerId, brandId);
    }

    /// <summary>Reads Idempotency-Key request header; returns null if absent or blank.</summary>
    private static string? GetIdempotencyKey(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var val))
        {
            var key = val.ToString().Trim();
            return string.IsNullOrEmpty(key) ? null : key;
        }
        return null;
    }
}
