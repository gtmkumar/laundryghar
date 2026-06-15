using core.Application.Identity.Onboarding.Commands.ActivateFranchise;
using core.Application.Identity.Onboarding.Commands.AddStore;
using core.Application.Identity.Onboarding.Commands.InviteOwner;
using core.Application.Identity.Onboarding.Commands.SaveCommercials;
using core.Application.Identity.Onboarding.Commands.SaveDetails;
using core.Application.Identity.Onboarding.Commands.StartOnboarding;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Franchises.Commands.CreateFranchise;
using core.Application.Identity.TenancyOrg.Franchises.Commands.DeleteFranchise;
using core.Application.Identity.TenancyOrg.Franchises.Commands.UpdateFranchise;
using core.Application.Identity.TenancyOrg.Franchises.Queries.GetFranchiseById;
using core.Application.Identity.TenancyOrg.Franchises.Queries.GetFranchises;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Franchise CRUD plus the guided, stage-gated franchise onboarding flow
/// (start → details → commercials → owner → store → activate). Thin: each method dispatches a
/// command/query through <see cref="IDispatcher"/>. Onboarding business logic (stage gating,
/// agreement creation, owner invite, activation checks) lives in the handlers.
/// </summary>
public class AdminFranchises : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/franchises";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Franchises").RequireAuthorization();

        // CRUD
        group.MapGet(GetAll).RequireAuthorization("permission:franchises.list");
        group.MapGet(GetById, "{id:guid}").RequireAuthorization("permission:franchises.read");
        group.MapPost(Create).RequireAuthorization("permission:franchises.create");
        group.MapPut(Update, "{id:guid}").RequireAuthorization("permission:franchises.update");
        group.MapDelete(Delete, "{id:guid}").RequireAuthorization("permission:franchises.delete");

        // Onboarding (guided, stage-gated)
        group.MapPost(StartOnboarding, "onboarding/start").RequireAuthorization("permission:franchises.create");
        group.MapGet(GetOnboarding, "{id:guid}/onboarding").RequireAuthorization("permission:franchises.read");
        group.MapPut(SaveDetails, "{id:guid}/onboarding/details").RequireAuthorization("permission:franchises.update");
        group.MapPut(SaveCommercials, "{id:guid}/onboarding/commercials").RequireAuthorization("permission:franchises.update");
        group.MapPost(InviteOwner, "{id:guid}/onboarding/owner").RequireAuthorization("permission:franchises.update");
        group.MapPost(AddStore, "{id:guid}/onboarding/store").RequireAuthorization("permission:stores.create");
        group.MapPost(Activate, "{id:guid}/onboarding/activate").RequireAuthorization("permission:franchises.update");
    }

    // ── CRUD ────────────────────────────────────────────────────────────────
    public static async Task<IResult> GetAll(Guid? brandId, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(new GetFranchisesQuery(brandId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<FranchiseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetFranchiseByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<FranchiseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateFranchiseRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateFranchiseCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/franchises/{data.Id}",
            new SingleResponse<FranchiseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateFranchiseRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateFranchiseCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<FranchiseDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteFranchiseCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    // ── Onboarding ────────────────────────────────────────────────────────────
    public static async Task<IResult> StartOnboarding(StartOnboardingRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new StartOnboardingCommand(req), ct);
        return Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetOnboarding(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetOnboardingStateQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SaveDetails(Guid id, SaveDetailsRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new SaveDetailsCommand(id, req), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SaveCommercials(Guid id, SaveCommercialsRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new SaveCommercialsCommand(id, req), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> InviteOwner(Guid id, InviteOwnerRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new InviteOwnerCommand(id, req), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> AddStore(Guid id, AddStoreRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new AddStoreCommand(id, req), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Activate(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new ActivateFranchiseCommand(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingStateDto> { Status = true, Data = data });
    }
}
