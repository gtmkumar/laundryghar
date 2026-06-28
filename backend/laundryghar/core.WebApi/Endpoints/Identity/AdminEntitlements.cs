using core.Application.Identity.Entitlements.Commands;
using core.Application.Identity.Entitlements.Dtos;
using core.Application.Identity.Entitlements.Queries;
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

        group.MapGet(GetBundles, "bundles").RequireAuthorization("permission:saas.read");
        group.MapGet(GetPlatformBilling, "platform-billing").RequireAuthorization("permission:saas.read");
        group.MapGet(GetBrandModules, "brands/{brandId:guid}/modules").RequireAuthorization("permission:saas.read");
        group.MapGet(GetPlatformSubscription, "brands/{brandId:guid}/platform-subscription").RequireAuthorization("permission:saas.read");
        group.MapPost(SetBrandModule, "brands/{brandId:guid}/modules").RequireAuthorization("permission:saas.manage");
        group.MapPost(ApplyBundle, "brands/{brandId:guid}/apply-bundle").RequireAuthorization("permission:saas.manage");
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
}
