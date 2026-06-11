using laundryghar.Logistics.Application.Assignments.Commands;
using laundryghar.Logistics.Application.Assignments.Dtos;
using laundryghar.Logistics.Application.Assignments.Queries;
using laundryghar.Logistics.Application.CapacityConfigs.Commands;
using laundryghar.Logistics.Application.CapacityConfigs.Dtos;
using laundryghar.Logistics.Application.CapacityConfigs.Queries;
using laundryghar.Logistics.Application.Riders.Commands;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.Logistics.Application.Riders.Queries;
using laundryghar.Logistics.Application.RiderOps;
using laundryghar.Logistics.Application.RiderCod;
using laundryghar.Logistics.Application.RiderSelf;
using laundryghar.SharedDataModel.Enums;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

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

        // ── Rider Ops (live board) ────────────────────────────────────────────
        // Read-only operational views over existing data: live location/status,
        // GPS breadcrumb trail, and per-rider throughput. Gated by rider.read.

        // GET /api/v1/admin/riders/live  — current location + ops status of every
        // (non-terminated) rider in scope, plus today's pickup/delivery counts.
        riders.MapGet("/live", async (
            [FromServices] ISender sender, CancellationToken ct, Guid? franchiseId = null) =>
        {
            var list = await sender.Send(new GetRidersLiveQuery(franchiseId), ct);
            return Results.Ok(new ListResponse<RiderLiveDto> { Status = true, Data = list });
        }).RequireAuthorization("permission:rider.read");

        // GET /api/v1/admin/riders/{id}/track?date=yyyy-MM-dd — GPS breadcrumb for a day.
        riders.MapGet("/{id:guid}/track", async (
            Guid id, [FromServices] ISender sender, CancellationToken ct, DateOnly? date = null) =>
        {
            var list = await sender.Send(new GetRiderTrackQuery(id, date), ct);
            return list is null
                ? Results.NotFound()
                : Results.Ok(new ListResponse<RiderTrackPointDto> { Status = true, Data = list });
        }).RequireAuthorization("permission:rider.read");

        // GET /api/v1/admin/riders/{id}/stats?from=&to= — throughput over a date range.
        riders.MapGet("/{id:guid}/stats", async (
            Guid id, [FromServices] ISender sender, CancellationToken ct,
            DateOnly? from = null, DateOnly? to = null) =>
        {
            var s = await sender.Send(new GetRiderStatsQuery(id, from, to), ct);
            return s is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderStatsDto> { Status = true, Data = s });
        }).RequireAuthorization("permission:rider.read");

        // ── COD cash reconciliation + settlement (Phase 3) ────────────────────
        // GET /api/v1/admin/riders/cod/outstanding — per-rider uncleared COD cash.
        riders.MapGet("/cod/outstanding", async (
            [FromServices] ISender sender, CancellationToken ct, Guid? franchiseId = null) =>
        {
            var list = await sender.Send(new GetCodOutstandingQuery(franchiseId), ct);
            return Results.Ok(new ListResponse<RiderCodSummaryDto> { Status = true, Data = list });
        }).RequireAuthorization("permission:rider.read");

        // GET /api/v1/admin/riders/{id}/cod — a rider's outstanding collections.
        riders.MapGet("/{id:guid}/cod", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var d = await sender.Send(new GetRiderCodDetailQuery(id), ct);
            return d is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderCodDetailDto> { Status = true, Data = d });
        }).RequireAuthorization("permission:rider.read");

        // GET /api/v1/admin/riders/{id}/settlements — settlement history.
        riders.MapGet("/{id:guid}/settlements", async (
            Guid id, [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetRiderSettlementsQuery(id, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new PaginatedListResponse<RiderSettlementDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:rider.read");

        // POST /api/v1/admin/riders/{id}/settle — clear all outstanding COD cash.
        riders.MapPost("/{id:guid}/settle", async (
            Guid id, SettleRiderCodRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var s = await sender.Send(new SettleRiderCodCommand(id, req, u.UserId), ct);
            return s is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderSettlementDto> { Status = true, Data = s });
        }).RequireAuthorization("permission:rider.settle");

        // ── Delivery Assignments — Proof Photos ───────────────────────────────
        // GET /api/v1/admin/rider-tasks/{id}/proof-photo
        // Streams the rider's proof-of-delivery photo for admin/dispatch inspection.
        // Routes under /rider-tasks/ to avoid confusion with the /rider-assignments/ CRUD group.
        var riderTasks = admin.MapGroup("/rider-tasks").WithTags("Admin - Rider Tasks");

        riderTasks.MapGet("/{id:guid}/proof-photo", async (
            Guid id, ICurrentUser u, [FromServices] ISender sender, CancellationToken ct) =>
        {
            var brandId = u.RequireBrandId();
            var result  = await sender.Send(new GetProofPhotoStreamQuery(id, brandId), ct);
            if (result is null) return Results.NotFound();

            return Results.Stream(
                result.Stream,
                contentType: result.ContentType,
                fileDownloadName: result.FileName,
                enableRangeProcessing: false);
        }).RequireAuthorization("permission:rider.read");

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

        // ── Per-order tasks (pickup/delivery legs) ────────────────────────────
        // GET /api/v1/rider/tasks/today
        riderSelf.MapGet("/tasks/today", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var list = await sender.Send(new GetMyTasksTodayQuery(userId, brandId), ct);
            return Results.Ok(new ListResponse<RiderTaskDto> { Status = true, Data = list });
        });

        // PATCH /api/v1/rider/tasks/{id}/status
        riderSelf.MapMethods("/tasks/{id:guid}/status", ["PATCH"], async (
            Guid id, RiderTaskStatusUpdateRequest req,
            HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(
                new UpdateMyTaskStatusCommand(id, userId, brandId, req.Status, req.Reason, req.Note), ct);
            return ToTaskResult(result);
        });

        // POST /api/v1/rider/tasks/{id}/verify-otp
        riderSelf.MapPost("/tasks/{id:guid}/verify-otp", async (
            Guid id, RiderTaskOtpVerifyRequest req,
            HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(new VerifyTaskOtpCommand(id, userId, brandId, req.Code), ct);
            return ToTaskResult(result);
        });

        // PATCH /api/v1/rider/duty
        // Toggle the authenticated rider's duty state.
        // RiderOnly — rider self-resolved from JWT; no id in path.
        riderSelf.MapMethods("/duty", ["PATCH"], async (
            SetDutyRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(new SetRiderDutyCommand(userId, brandId, req.OnDuty), ct);

            return result.Outcome switch
            {
                "ok" => Results.Ok(new SingleResponse<DutyToggleResponse>
                        { Status = true, Data = result.Data }),
                _    => Results.NotFound()
            };
        });

        // POST /api/v1/rider/tasks/{id}/proof-photo
        // Rider uploads a proof-of-delivery photo. Optional — does not block completion.
        // Stores the storage key in delivery_assignments.proof_photo_s3_key.
        // DisableAntiforgery() is required for IFormFile in .NET 10 minimal APIs.
        riderSelf.MapPost("/tasks/{id:guid}/proof-photo", async (
            Guid id,
            IFormFile file,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(
                new UploadProofPhotoCommand(id, userId, brandId, file), ct);
            return ToTaskResult(result);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024)); // 10 MB + envelope overhead

        // POST /api/v1/rider/tasks/{id}/inspection
        // Rider submits pickup garment inspection evidence (condition photos + flags).
        // Front photo is required; back is optional.  Conditions are a JSON string
        // matching ConditionFlags: { stains, tears, missingButtons }.
        // Notes are capped at 500 chars.
        // DisableAntiforgery() required for multipart in .NET 10 minimal APIs.
        riderSelf.MapPost("/tasks/{id:guid}/inspection", async (
            Guid id,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            // Parse multipart fields manually — IFormFile injection doesn't work for
            // named optional files alongside non-file fields in minimal APIs.
            var form = ctx.Request.Form;

            var frontFile = form.Files["front"];
            if (frontFile is null)
                return Results.BadRequest(new Response { Status = false,
                    Message = new Message { ResponseMessage = "front photo is required." } });

            var backFile = form.Files.GetFile("back"); // null when absent

            // Parse conditions JSON string from the form field.
            InspectionConditions conditions;
            var condRaw = form["conditions"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(condRaw))
                return Results.BadRequest(new Response { Status = false,
                    Message = new Message { ResponseMessage = "conditions field is required." } });

            try
            {
                var parsed = JsonSerializer.Deserialize<ConditionsFlagsInput>(condRaw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                conditions = new InspectionConditions(
                    parsed?.Stains         ?? false,
                    parsed?.Tears          ?? false,
                    parsed?.MissingButtons ?? false);
            }
            catch
            {
                return Results.BadRequest(new Response { Status = false,
                    Message = new Message { ResponseMessage = "conditions must be valid JSON with stains/tears/missingButtons booleans." } });
            }

            var notes = form["notes"].FirstOrDefault()?.Trim();
            if (notes?.Length > 500)
                return Results.BadRequest(new Response { Status = false,
                    Message = new Message { ResponseMessage = "notes must not exceed 500 characters." } });

            var result = await sender.Send(
                new SubmitPickupInspectionCommand(id, userId, brandId, frontFile, backFile, conditions, notes), ct);

            if (result.Outcome == "inspection_ok" && result.Error is not null)
            {
                var data = JsonSerializer.Deserialize<RiderInspectionResult>(result.Error);
                return Results.Ok(new SingleResponse<RiderInspectionResult> { Status = true, Data = data! });
            }

            return ToTaskResult(result);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(22 * 1024 * 1024)); // 2 × 10 MB photos + overhead

        // GET /api/v1/rider/tasks?date=YYYY-MM-DD
        // Returns the rider's tasks completed on the given calendar date (IST boundary).
        // Same RiderTaskDto shape as /tasks/today. When date is absent callers should
        // use /tasks/today which includes open tasks.
        riderSelf.MapGet("/tasks", async (
            HttpContext ctx, ISender sender, CancellationToken ct,
            string? date = null) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            // Require explicit date — this endpoint is the earnings drill-down,
            // not a general task list. /tasks/today handles the live feed.
            if (!DateOnly.TryParse(date, out var parsedDate))
                return Results.BadRequest(new Response { Status = false,
                    Message = new Message { ResponseMessage = "date query param is required in YYYY-MM-DD format." } });

            var list = await sender.Send(new GetMyTasksByDateQuery(userId, brandId, parsedDate), ct);
            return Results.Ok(new ListResponse<RiderTaskDto> { Status = true, Data = list });
        });

        // ── Push Tokens ───────────────────────────────────────────────────────
        // Expo push token registration / deactivation for rider devices.
        // Both endpoints are RiderOnly (JWT sub = userId, user_type = rider).
        // POST upserts on the unique token column — re-registering reactivates.
        // DELETE deactivates on logout so the Worker skips signed-out devices.

        // POST /api/v1/rider/push-token
        riderSelf.MapPost("/push-token", async (
            RiderRegisterPushTokenRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            await sender.Send(
                new RegisterRiderPushTokenCommand(userId, brandId, req.Token, req.Platform), ct);

            return Results.Ok(new Response { Status = true });
        });

        // DELETE /api/v1/rider/push-token
        riderSelf.MapDelete("/push-token", async (
            [FromBody] RiderDeactivatePushTokenRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = GetRiderUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();

            await sender.Send(
                new DeactivateRiderPushTokenCommand(userId, req.Token), ct);

            return Results.Ok(new Response { Status = true });
        });

        // ── Earnings / Payouts ────────────────────────────────────────────────
        // GET /api/v1/rider/payouts?days=30
        // Returns a per-day earnings breakdown (completed legs with a persisted
        // payout_amount) plus a summary (total, avgPerTask) for the rolling window.
        riderSelf.MapGet("/payouts", async (
            HttpContext ctx, ISender sender, CancellationToken ct,
            int days = 30) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(new GetMyPayoutsQuery(userId, brandId, days), ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderPayoutSummaryDto> { Status = true, Data = result });
        });

        // ── COD cash self-summary ─────────────────────────────────────────────
        // GET /api/v1/rider/cash/summary
        // Returns the authenticated rider's outstanding COD balance (cash in hand),
        // last settlement timestamp, and up to 10 recent settlements.
        riderSelf.MapGet("/cash/summary", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var userId  = GetRiderUserId(ctx);
            var brandId = GetRiderBrandId(ctx);
            if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

            var result = await sender.Send(new GetMyCashSummaryQuery(userId, brandId), ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RiderCashSummaryDto> { Status = true, Data = result });
        });
    }

    // Maps a RiderTaskResult onto an HTTP result: ok→200, not_found→404, conflict→400.
    private static IResult ToTaskResult(RiderTaskResult result) => result.Outcome switch
    {
        "ok"       => Results.Ok(new SingleResponse<RiderTaskDto> { Status = true, Data = result.Task }),
        "conflict" => Results.BadRequest(new Response
        {
            Status  = false,
            Message = new Message { ResponseMessage = result.Error ?? "Action not allowed." }
        }),
        _          => Results.NotFound(),
    };

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

// ── Request DTOs (local to this endpoint file) ────────────────────────────────

/// <param name="Token">Expo push token (ExponentPushToken[…] or ExpoPushToken[…]).</param>
/// <param name="Platform">"ios" or "android".</param>
public sealed record RiderRegisterPushTokenRequest(string Token, string Platform);

/// <param name="Token">The Expo push token to deactivate.</param>
public sealed record RiderDeactivatePushTokenRequest(string Token);
