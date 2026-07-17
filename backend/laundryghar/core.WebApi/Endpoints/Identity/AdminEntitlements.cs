using core.Application.Identity.Entitlements.Commands;
using core.Application.Identity.Entitlements.Dtos;
using core.Application.Identity.Entitlements.Queries;
using laundryghar.Utilities.Caching;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — PaaS entitlement console (/api/v1/admin/entitlements): per-brand module
/// licensing + plan bundles. Platform-level (gated by saas.read / saas.manage).
/// See docs/rbac-entitlement-plan.md.
/// </summary>
public class AdminEntitlements : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/entitlements";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Entitlements").RequireAuthorization();

        // Bundle definitions are seed-defined and runtime-read-only (ApplyBundle changes a
        // brand's modules, never the bundles) — TTL-only cache, no eviction needed.
        group.MapGet(GetBundles, "bundles").RequireAuthorization("permission:saas.read")
             .CacheSharedOutput("saas:bundles", TimeSpan.FromMinutes(30));
        group.MapGet(GetPlatformBilling, "platform-billing").RequireAuthorization("permission:saas.read");
        group.MapGet(GetBrandModules, "brands/{brandId:guid}/modules").RequireAuthorization("permission:saas.read");
        group.MapGet(GetPlatformSubscription, "brands/{brandId:guid}/platform-subscription").RequireAuthorization("permission:saas.read");
        group.MapPost(CancelPlatformSubscription, "brands/{brandId:guid}/platform-subscription/cancel").RequireAuthorization("permission:saas.manage");
        group.MapPost(SetBrandModule, "brands/{brandId:guid}/modules").RequireAuthorization("permission:saas.manage");
        group.MapPost(ApplyBundle, "brands/{brandId:guid}/apply-bundle").RequireAuthorization("permission:saas.manage");
        group.MapPost(SetInvoiceStatus, "brand-platform-invoices/{invoiceId:guid}/status").RequireAuthorization("permission:saas.manage");
        group.MapPost(CreateInvoicePaymentLink, "brand-platform-invoices/{invoiceId:guid}/payment-link").RequireAuthorization("permission:saas.manage");
        group.MapPost(SyncInvoicePayment, "brand-platform-invoices/{invoiceId:guid}/sync-payment").RequireAuthorization("permission:saas.manage");
    }

    public static async Task<IResult> GetBundles(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetModuleBundlesQuery(), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<ModuleBundleDto>> { Status = true, Data = data });
    }

    public static async Task<IResult> GetBrandModules(Guid brandId, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBrandEntitlementsQuery(brandId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<BrandEntitlementsDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetPlatformBilling(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetPlatformBillingSummaryQuery(), ct);
        return Results.Ok(new SingleResponse<PlatformBillingSummaryDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetPlatformSubscription(Guid brandId, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBrandPlatformSubscriptionQuery(brandId), ct);
        // 200 with null data = brand is not on a priced tier yet (UI shows "no plan").
        return Results.Ok(new SingleResponse<BrandPlatformSubscriptionDto?> { Status = true, Data = data });
    }

    public static async Task<IResult> SetBrandModule(Guid brandId, SetBrandModuleRequest req,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.SendAsync(new SetBrandModuleCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> ApplyBundle(Guid brandId, ApplyBundleRequest req,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.SendAsync(new ApplyBundleToBrandCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> SetInvoiceStatus(Guid invoiceId, SetInvoiceStatusRequest req,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetBrandPlatformInvoiceStatusCommand(invoiceId, req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> CancelPlatformSubscription(Guid brandId, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new CancelBrandPlatformSubscriptionCommand(brandId, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> CreateInvoicePaymentLink(Guid invoiceId, IDispatcher dispatcher, CancellationToken ct)
    {
        var url = await dispatcher.SendAsync(new CreateBrandPlatformInvoicePaymentLinkCommand(invoiceId), ct);
        return url is null ? Results.NotFound() : Results.Ok(new SingleResponse<string> { Status = true, Data = url });
    }

    public static async Task<IResult> SyncInvoicePayment(Guid invoiceId, IDispatcher dispatcher, CancellationToken ct)
    {
        var status = await dispatcher.SendAsync(new SyncBrandPlatformInvoicePaymentCommand(invoiceId), ct);
        return status is null ? Results.NotFound() : Results.Ok(new SingleResponse<string> { Status = true, Data = status });
    }
}
