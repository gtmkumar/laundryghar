using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Logistics.Cod;
using operations.Application.Logistics.RiderOps.Dtos;
using operations.Application.Logistics.RiderOps.Queries.GetRiderStats;
using operations.Application.Logistics.RiderOps.Queries.GetRiderTrack;
using operations.Application.Logistics.RiderOps.Queries.GetRidersLive;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Commands.DeactivateRider;
using operations.Application.Logistics.Riders.Commands.RejectRiderKyc;
using operations.Application.Logistics.Riders.Commands.UpdateRider;
using operations.Application.Logistics.Riders.Commands.Verification;
using operations.Application.Logistics.Riders.Commands.VerifyRiderKyc;
using operations.Application.Logistics.Riders.Dtos;
using operations.Application.Logistics.Riders.Queries.GetRiderById;
using operations.Application.Logistics.Riders.Queries.GetRiders;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Riders: CRUD, KYC verify/reject, vehicle review, and the live ops board
/// (location/track/stats). Brand defense-in-depth: handlers filter by brandId so
/// cross-brand reads return 404. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class RidersAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/riders";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Riders").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:rider.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:rider.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateRiderRequest>>()
            .RequireAuthorization("permission:rider.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdateRiderRequest>>()
            .RequireAuthorization("permission:rider.manage");
        group.MapPost(Deactivate, "/{id:guid}/deactivate").RequireAuthorization("permission:rider.manage");
        group.MapPost(Verify, "/{id:guid}/verify").RequireAuthorization("permission:rider.verify");
        group.MapPost(Reject, "/{id:guid}/reject")
            .AddEndpointFilter<ValidationFilter<RejectRiderRequest>>()
            .RequireAuthorization("permission:rider.verify");

        // Driver verification queue — KYC status + vehicle gate + documents.
        group.MapGet(GetVerification, "/{id:guid}/verification").RequireAuthorization("permission:rider.read");
        group.MapPost(ApproveVehicle, "/{id:guid}/vehicle/approve").RequireAuthorization("permission:rider.verify");
        group.MapPost(RejectVehicle, "/{id:guid}/vehicle/reject")
            .AddEndpointFilter<ValidationFilter<RejectRiderRequest>>()
            .RequireAuthorization("permission:rider.verify");

        // Rider Ops (live board) — read-only operational views.
        group.MapGet(GetLive, "/live").RequireAuthorization("permission:rider.read");
        group.MapGet(GetTrack, "/{id:guid}/track").RequireAuthorization("permission:rider.read");
        group.MapGet(GetStats, "/{id:guid}/stats").RequireAuthorization("permission:rider.read");

        // Rider Cash / COD reconciliation — uncleared cash detail, settle-all, and history.
        group.MapGet(GetCod, "/{id:guid}/cod").RequireAuthorization("permission:rider.read");
        group.MapPost(Settle, "/{id:guid}/settle").RequireAuthorization("permission:rider.settle");
        group.MapGet(GetSettlements, "/{id:guid}/settlements").RequireAuthorization("permission:rider.read");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null, Guid? franchiseId = null,
        string? search = null, string? kycStatus = null, string? sort = null)
    {
        var r = await dispatcher.QueryAsync(new GetRidersQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize,
            status, franchiseId, search, kycStatus, sort), ct);
        return Results.Ok(new PaginatedListResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetRiderByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateRiderCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/riders/{r.Id}",
            new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateRiderCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Deactivate(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new DeactivateRiderCommand(id, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Verify(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new VerifyRiderKycCommand(id, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Reject(Guid id, RejectRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new RejectRiderKycCommand(id, req.Reason, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetVerification(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetRiderVerificationQuery(id), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderVerificationView> { Status = true, Data = r });
    }

    public static async Task<IResult> ApproveVehicle(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewRiderVehicleCommand(id, true, null, u.UserId), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderVerificationView> { Status = true, Data = r });
    }

    public static async Task<IResult> RejectVehicle(Guid id, RejectRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewRiderVehicleCommand(id, false, req.Reason, u.UserId), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderVerificationView> { Status = true, Data = r });
    }

    public static async Task<IResult> GetLive(IDispatcher dispatcher, CancellationToken ct, Guid? franchiseId = null)
    {
        var list = await dispatcher.QueryAsync(new GetRidersLiveQuery(franchiseId), ct);
        return Results.Ok(new ListResponse<RiderLiveDto> { Status = true, Data = list });
    }

    public static async Task<IResult> GetTrack(Guid id, IDispatcher dispatcher, CancellationToken ct, DateOnly? date = null)
    {
        var list = await dispatcher.QueryAsync(new GetRiderTrackQuery(id, date), ct);
        return list is null
            ? Results.NotFound()
            : Results.Ok(new ListResponse<RiderTrackPointDto> { Status = true, Data = list });
    }

    public static async Task<IResult> GetStats(Guid id, IDispatcher dispatcher, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null)
    {
        var s = await dispatcher.QueryAsync(new GetRiderStatsQuery(id, from, to), ct);
        return s is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderStatsDto> { Status = true, Data = s });
    }

    public static async Task<IResult> GetCod(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetRiderCodDetailQuery(u.RequireBrandId(), id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderCodDetail> { Status = true, Data = r });
    }

    public static async Task<IResult> Settle(Guid id, SettleRiderPayload? req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new SettleRiderCodCommand(u.RequireBrandId(), id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderSettlementDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetSettlements(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetRiderSettlementsQuery(
            u.RequireBrandId(), id, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<RiderSettlementDto> { Status = true, Data = r });
    }
}
