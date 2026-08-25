using core.Application.Identity.ProviderOnboarding.Commands;
using core.Application.Identity.ProviderOnboarding.Dtos;
using core.Application.Identity.ProviderOnboarding.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// §9's onboarding wizard: "locations, catalog seed, staff invites, gateway link → live on
/// sub-domain."
///
/// <para>Note what is NOT here: there is no "create location" or "invite staff" endpoint. Those
/// already exist, are already permission-gated, and already validate their own input. Duplicating
/// them behind a wizard prefix would mean two code paths for the same operation, and the second one
/// drifting. The wizard tells you where you are and what is left; the existing endpoints do the
/// work.</para>
/// </summary>
public class AdminProviderOnboarding : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/provider-onboarding";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Onboarding").RequireAuthorization();

        group.MapGet(Get, "").RequireAuthorization("permission:settings.read");
        group.MapPost(SaveDraft, "draft").RequireAuthorization("permission:settings.read");
        group.MapPost(Skip, "skip").RequireAuthorization("permission:settings.manage");
        group.MapPost(GoLive, "go-live").RequireAuthorization("permission:settings.manage");
    }

    /// <summary>Where the provider is, recomputed from their account every time.</summary>
    public static async Task<IResult> Get(ICurrentTenant tenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var state = await dispatcher.QueryAsync(new GetOnboardingStateQuery(brandId), ct);
        return state is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = state });
    }

    /// <summary>Gated on settings.READ, not manage: saving a half-typed form is not a change to the
    /// business, and requiring the higher permission would make the wizard lose work for exactly the
    /// staff most likely to be filling it in.</summary>
    public static async Task<IResult> SaveDraft(
        SaveOnboardingDraftRequest req, ICurrentTenant tenant, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        await dispatcher.SendAsync(new SaveOnboardingDraftCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> Skip(
        SkipOnboardingStepRequest req, ICurrentTenant tenant, ICurrentUser user,
        IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        await dispatcher.SendAsync(new SkipOnboardingStepCommand(brandId, req, user.UserId), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> GoLive(
        ICurrentTenant tenant, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        if (tenant.BrandId is not { } brandId) return Results.BadRequest();

        var domain = await dispatcher.SendAsync(new GoLiveCommand(brandId, user.UserId), ct);
        return domain is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<string> { Status = true, Data = domain });
    }
}
