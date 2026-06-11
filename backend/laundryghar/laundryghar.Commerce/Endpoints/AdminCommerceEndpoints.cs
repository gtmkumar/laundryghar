using laundryghar.Commerce.Application;
using laundryghar.Commerce.Application.Admin.Coupons;
using laundryghar.Commerce.Application.Admin.LoyaltyPrograms;
using laundryghar.Commerce.Application.Admin.Packages;
using laundryghar.Commerce.Application.Admin.PaymentMethods;
using laundryghar.Commerce.Application.Admin.Payments;
using laundryghar.Commerce.Application.Admin.Promotions;
using laundryghar.Commerce.Application.Admin.Subscriptions;
using laundryghar.Commerce.Application.Admin.Wallet;
using MediatR;

namespace laundryghar.Commerce.Endpoints;

/// <summary>
/// Admin commerce endpoints.
/// All require token_use=user + specific permission codes (or platform_admin bypass).
/// Brand predicate applied in every handler: BrandId == _user.RequireBrandId().
/// </summary>
public static class AdminCommerceEndpoints
{
    public static RouteGroupBuilder MapAdminCommerceEndpoints(this RouteGroupBuilder group)
    {
        // ── Payment Methods ───────────────────────────────────────────────────
        var pm = group.MapGroup("/payment-methods").WithTags("Admin - Commerce - Payment Methods");

        pm.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetPaymentMethodsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PaymentMethodDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:paymentmethod.manage");

        pm.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPaymentMethodByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:paymentmethod.manage");

        pm.MapPost("/", async (CreatePaymentMethodRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePaymentMethodCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/payment-methods/{r.Id}", new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:paymentmethod.manage");

        pm.MapPut("/{id:guid}", async (Guid id, UpdatePaymentMethodRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdatePaymentMethodCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:paymentmethod.manage");

        pm.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeletePaymentMethodCommand(id), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:paymentmethod.manage");

        // ── Packages ──────────────────────────────────────────────────────────
        var pkgs = group.MapGroup("/packages").WithTags("Admin - Commerce - Packages");

        pkgs.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetPackagesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PackageDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:packages.manage");

        pkgs.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPackageByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PackageDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:packages.manage");

        pkgs.MapPost("/", async (CreatePackageRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePackageCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/packages/{r.Id}", new SingleResponse<PackageDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:packages.manage");

        pkgs.MapPut("/{id:guid}", async (Guid id, UpdatePackageRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdatePackageCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PackageDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:packages.manage");

        pkgs.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeletePackageCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:packages.manage");

        // ── Promotions ────────────────────────────────────────────────────────
        var promos = group.MapGroup("/promotions").WithTags("Admin - Commerce - Promotions");

        promos.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetPromotionsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PromotionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:promotions.manage");

        promos.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPromotionByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PromotionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:promotions.manage");

        promos.MapPost("/", async (CreatePromotionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePromotionCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/promotions/{r.Id}", new SingleResponse<PromotionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:promotions.manage");

        promos.MapPut("/{id:guid}", async (Guid id, UpdatePromotionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdatePromotionCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PromotionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:promotions.manage");

        promos.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeletePromotionCommand(id), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:promotions.manage");

        // ── Coupons ───────────────────────────────────────────────────────────
        var coupons = group.MapGroup("/coupons").WithTags("Admin - Commerce - Coupons");

        coupons.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetCouponsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<CouponDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:coupons.manage");

        coupons.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetCouponByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CouponDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:coupons.manage");

        coupons.MapPost("/", async (CreateCouponRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateCouponCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/coupons/{r.Id}", new SingleResponse<CouponDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:coupons.manage");

        coupons.MapPut("/{id:guid}", async (Guid id, UpdateCouponRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateCouponCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CouponDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:coupons.manage");

        coupons.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteCouponCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:coupons.manage");

        // ── Loyalty Programs ──────────────────────────────────────────────────
        var loyalty = group.MapGroup("/loyalty-programs").WithTags("Admin - Commerce - Loyalty Programs");

        loyalty.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetLoyaltyProgramsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<LoyaltyProgramDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:loyalty.manage");

        loyalty.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetLoyaltyProgramByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:loyalty.manage");

        loyalty.MapPost("/", async (CreateLoyaltyProgramRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateLoyaltyProgramCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/loyalty-programs/{r.Id}", new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:loyalty.manage");

        loyalty.MapPut("/{id:guid}", async (Guid id, UpdateLoyaltyProgramRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateLoyaltyProgramCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:loyalty.manage");

        loyalty.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteLoyaltyProgramCommand(id), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:loyalty.manage");

        // ── Admin Payments ────────────────────────────────────────────────────
        var payments = group.MapGroup("/payments").WithTags("Admin - Commerce - Payments");

        payments.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? customerId = null) =>
        {
            var r = await sender.Send(new GetAdminPaymentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, customerId), ct);
            return Results.Ok(new PaginatedListResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:payment.read");

        payments.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAdminPaymentByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:payment.read");

        payments.MapPost("/", async (HttpContext http, RecordOfflinePaymentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            // H3b: honour Idempotency-Key header; fallback to body field then server-derived key.
            var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var hdrKey)
                ? hdrKey.FirstOrDefault()
                : req.IdempotencyKey;

            var reqWithKey = req with { IdempotencyKey = idempotencyKey };
            var r = await sender.Send(new RecordOfflinePaymentCommand(reqWithKey, u.UserId), ct);
            return Results.Created($"/api/v1/admin/payments/{r.PaymentId}", new SingleResponse<OfflinePaymentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:payment.record");

        payments.MapPost("/refunds", async (IssueRefundRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new IssueRefundCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/payments/refunds/{r.Id}", new SingleResponse<PaymentRefundDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:payment.refund");

        // ── Admin Wallet ──────────────────────────────────────────────────────
        var wallets = group.MapGroup("/wallets").WithTags("Admin - Commerce - Wallets");

        wallets.MapGet("/{customerId:guid}", async (Guid customerId, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetCustomerWalletQuery(customerId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WalletAccountDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:wallet.read");

        wallets.MapGet("/{customerId:guid}/transactions", async (Guid customerId, ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetCustomerWalletTransactionsQuery(customerId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<WalletTransactionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:wallet.read");

        wallets.MapPost("/adjust", async (AdminWalletAdjustRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AdminWalletAdjustCommand(req, u.UserId), ct);
            return Results.Ok(new SingleResponse<WalletTransactionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:wallet.adjust");

        // ── Subscription Plans ─────────────────────────────────────────────────
        var subPlans = group.MapGroup("/subscription-plans").WithTags("Admin - Commerce - Subscription Plans");

        subPlans.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetSubscriptionPlansQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<SubscriptionPlanDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.manage");

        subPlans.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetSubscriptionPlanByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.manage");

        subPlans.MapPost("/", async (CreateSubscriptionPlanRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateSubscriptionPlanCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/subscription-plans/{r.Id}", new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.manage");

        subPlans.MapPut("/{id:guid}", async (Guid id, UpdateSubscriptionPlanRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateSubscriptionPlanCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.manage");

        subPlans.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteSubscriptionPlanCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:subscription.manage");

        subPlans.MapPatch("/{id:guid}/status", async (Guid id, PatchSubscriptionPlanStatusRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new PatchSubscriptionPlanStatusCommand(id, req.Status, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SubscriptionPlanDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.manage");

        // ── Admin: Customer Subscriptions (read) ───────────────────────────────
        var custSubs = group.MapGroup("/subscriptions").WithTags("Admin - Commerce - Customer Subscriptions");

        custSubs.MapGet("/", async ([FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? customerId = null, string? status = null) =>
        {
            var r = await sender.Send(new GetCustomerSubscriptionsAdminQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, customerId, status), ct);
            return Results.Ok(new PaginatedListResponse<CustomerSubscriptionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:subscription.read");

        return group;
    }
}
