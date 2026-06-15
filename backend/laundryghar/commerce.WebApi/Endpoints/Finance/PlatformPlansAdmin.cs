using commerce.Application.Finance.Subscriptions.Commands;
using commerce.Application.Finance.Subscriptions.Dtos;
using commerce.Application.Finance.Subscriptions.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Finance;

/// <summary>Admin — SaaS platform plans (platform_admin only). Reads gated by saas.read; mutations by saas.manage.</summary>
public class PlatformPlansAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/platform-plans";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - SaaS - Platform Plans");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:saas.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:saas.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreatePlatformPlanRequest>>()
            .RequireAuthorization("permission:saas.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdatePlatformPlanRequest>>()
            .RequireAuthorization("permission:saas.manage");
        group.MapPatch(PatchStatus, "/{id:guid}/status")
            .AddEndpointFilter<ValidationFilter<PatchPlatformPlanStatusRequest>>()
            .RequireAuthorization("permission:saas.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:saas.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetPlatformPlansQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<PlatformPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPlatformPlanByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PlatformPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(
        CreatePlatformPlanRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePlatformPlanCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/platform-plans/{r.Id}",
            new SingleResponse<PlatformPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(
        Guid id, UpdatePlatformPlanRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePlatformPlanCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PlatformPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> PatchStatus(
        Guid id, PatchPlatformPlanStatusRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new PatchPlatformPlanStatusCommand(id, req.Status, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PlatformPlanDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeletePlatformPlanCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
