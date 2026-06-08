using laundryghar.Logistics.Application.Assignments.Commands;
using laundryghar.Logistics.Application.Assignments.Dtos;
using laundryghar.Logistics.Application.Assignments.Queries;
using laundryghar.Logistics.Application.CapacityConfigs.Commands;
using laundryghar.Logistics.Application.CapacityConfigs.Dtos;
using laundryghar.Logistics.Application.CapacityConfigs.Queries;
using laundryghar.Logistics.Application.Riders.Commands;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.Logistics.Application.Riders.Queries;
using laundryghar.Logistics.Application.RiderSelf;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Endpoints;

public static class LogisticsEndpoints
{
    public static WebApplication MapLogisticsEndpoints(this WebApplication app)
    {
        MapAdminEndpoints(app);
        MapRiderSelfEndpoints(app);
        return app;
    }

    // ─── Admin / Dispatch endpoints (/api/v1/admin/*) ────────────────────────
    // Brand defense-in-depth: every handler calls _user.RequireBrandId() and
    // filters by brandId, so cross-brand reads return 404 (not 403), matching
    // the expected acceptance gate.
    private static void MapAdminEndpoints(WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization();

        // ── Riders ────────────────────────────────────────────────────────────
        var riders = admin.MapGroup("/riders").WithTags("Admin - Riders");

        riders.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            string? status = null, Guid? franchiseId = null,
            string? search = null, string? kycStatus = null, string? sort = null) =>
        {
            var r = await sender.Send(new GetRidersQuery(
                page < 1 ? 1 : page,
                pageSize < 1 ? 20 : pageSize,
                status,
                franchiseId,
                search,
                kycStatus,
                sort), ct);
            return Results.Ok(new PaginatedListResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.read");

        riders.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetRiderByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.read");

        riders.MapPost("/", async (CreateRiderRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateRiderCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/riders/{r.Id}",
                new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.manage");

        riders.MapPut("/{id:guid}", async (Guid id, UpdateRiderRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateRiderCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.manage");

        riders.MapPost("/{id:guid}/deactivate", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeactivateRiderCommand(id, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.manage");

        riders.MapPost("/{id:guid}/verify", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new VerifyRiderKycCommand(id, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.verify");

        riders.MapPost("/{id:guid}/reject", async (
            Guid id, RejectRiderRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new RejectRiderKycCommand(id, req.Reason, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.verify");

        // ── Rider Assignments ─────────────────────────────────────────────────
        var assignments = admin.MapGroup("/rider-assignments").WithTags("Admin - Rider Assignments");

        assignments.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? riderId = null, string? status = null, DateOnly? shiftDate = null) =>
        {
            var r = await sender.Send(
                new GetAssignmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, riderId, status, shiftDate), ct);
            return Results.Ok(new PaginatedListResponse<RiderAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.assignment.read");

        assignments.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAssignmentByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.assignment.read");

        assignments.MapPost("/", async (CreateRiderAssignmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateRiderAssignmentCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/rider-assignments/{r.Id}",
                new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.assignment.manage");

        assignments.MapPut("/{id:guid}", async (Guid id, UpdateRiderAssignmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateRiderAssignmentCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.assignment.manage");

        // ── Rider Capacity Configs ─────────────────────────────────────────────
        var capacity = admin.MapGroup("/rider-capacity-configs").WithTags("Admin - Rider Capacity Configs");

        capacity.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? riderId = null, string? status = null) =>
        {
            var r = await sender.Send(
                new GetCapacityConfigsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, riderId, status), ct);
            return Results.Ok(new PaginatedListResponse<RiderCapacityConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.capacity.manage");

        capacity.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetCapacityConfigByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.capacity.manage");

        capacity.MapPost("/", async (CreateCapacityConfigRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateCapacityConfigCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/rider-capacity-configs/{r.Id}",
                new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.capacity.manage");

        capacity.MapPut("/{id:guid}", async (Guid id, UpdateCapacityConfigRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateCapacityConfigCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderCapacityConfigDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.capacity.manage");
    }

    // ─── Rider self-service endpoints (/api/v1/rider/*) ─────────────────────
    // All require "RiderOnly" policy (token_use=user, user_type=rider).
    // Self-filtering: rider id derived from JWT sub, brand_id from JWT brand_id claim.
    private static void MapRiderSelfEndpoints(WebApplication app)
    {
        var riderSelf = app.MapGroup("/api/v1/rider")
            .RequireAuthorization("RiderOnly")
            .WithTags("Rider Self-Service");

        // GET /api/v1/rider/me
        riderSelf.MapGet("/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId = GetRiderUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var r = await sender.Send(new GetMyRiderProfileQuery(userId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
        });

        // GET /api/v1/rider/assignments/today
        riderSelf.MapGet("/assignments/today", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var list = await sender.Send(new GetMyAssignmentsTodayQuery(userId, brandId), ct);
            return Results.Ok(new ListResponse<RiderAssignmentDto> { Status = true, Data = list });
        });

        // POST /api/v1/rider/location/ping
        riderSelf.MapPost("/location/ping", async (
            List<LocationPingInput> pings,
            HttpContext ctx,
            ISender sender,
            LaundryGharDbContext db,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            if (pings is null || pings.Count == 0)
                return Results.BadRequest(new Response
                {
                    Status  = false,
                    Message = new Message { ResponseMessage = "Ping batch must not be empty." }
                });

            // Resolve rider id from user_id
            var rider = await db.Riders
                .Where(r => r.UserId == userId && r.BrandId == brandId)
                .Select(r => new { r.Id })
                .FirstOrDefaultAsync(ct);

            if (rider is null) return Results.NotFound();

            var result = await sender.Send(
                new BatchLocationPingCommand(rider.Id, brandId, userId, pings), ct);

            return Results.Ok(new SingleResponse<PingBatchResponse> { Status = true, Data = result });
        });

        // PATCH /api/v1/rider/assignments/{id}/status
        riderSelf.MapMethods("/assignments/{id:guid}/status", ["PATCH"], async (
            Guid id,
            RiderAssignmentStatusUpdateRequest req,
            HttpContext ctx,
            ISender sender,
            LaundryGharDbContext db,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            // Resolve rider id
            var rider = await db.Riders
                .Where(r => r.UserId == userId && r.BrandId == brandId)
                .Select(r => new { r.Id })
                .FirstOrDefaultAsync(ct);

            if (rider is null) return Results.NotFound();

            var result = await sender.Send(
                new UpdateMyAssignmentStatusCommand(id, rider.Id, brandId, req.Status), ct);

            // If null: either assignment doesn't exist OR belongs to a different rider → 404
            return result is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = result });
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Guid GetRiderUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var g) ? g : Guid.Empty;
    }

    private static Guid GetRiderBrandId(HttpContext ctx)
    {
        var brandClaim = ctx.User.FindFirstValue("brand_id");
        return Guid.TryParse(brandClaim, out var g) ? g : Guid.Empty;
    }
}
