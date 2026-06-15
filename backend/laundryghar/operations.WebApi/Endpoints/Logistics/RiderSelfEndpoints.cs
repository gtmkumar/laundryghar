using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Logistics.Assignments.Dtos;
using operations.Application.Logistics.Riders.Dtos;
using operations.Application.Logistics.RiderSelf.Commands.BatchLocationPing;
using operations.Application.Logistics.RiderSelf.Commands.OfferActions;
using operations.Application.Logistics.RiderSelf.Commands.RiderPayoutRequests;
using operations.Application.Logistics.RiderSelf.Commands.RiderPushToken;
using operations.Application.Logistics.RiderSelf.Commands.SetRiderDuty;
using operations.Application.Logistics.RiderSelf.Commands.SubmitPickupInspection;
using operations.Application.Logistics.RiderSelf.Commands.UpdateMyAssignmentStatus;
using operations.Application.Logistics.RiderSelf.Commands.UpdateMyTaskStatus;
using operations.Application.Logistics.RiderSelf.Commands.UploadProofPhoto;
using operations.Application.Logistics.RiderSelf.Commands.UploadRiderDocument;
using operations.Application.Logistics.RiderSelf.Commands.VerifyTaskOtp;
using operations.Application.Logistics.RiderSelf.Dtos;
using operations.Application.Logistics.RiderSelf.Queries.GetMyAssignmentsToday;
using operations.Application.Logistics.RiderSelf.Queries.GetMyPayouts;
using operations.Application.Logistics.RiderSelf.Queries.GetMyRiderProfile;
using operations.Application.Logistics.RiderSelf.Queries.GetMyTasksByDate;
using operations.Application.Logistics.RiderSelf.Queries.GetMyTasksToday;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Rider self-service lane (/api/v1/rider/*). Group-gated by the "RiderOnly" policy
/// (token_use=user, user_type=rider). Self-filtering: the rider id is derived from the
/// JWT sub (UserId) and brand_id claim (BrandId) via <see cref="ICurrentUser"/>.
///
/// Permission lanes (combined with RiderOnly):
///   session lane (me/duty/location/push-token) — RiderOnly alone, so revoking task
///     permissions never bricks login or live tracking.
///   read lane   — permission:rider.tasks.read   (task/assignment/earnings views)
///   write lane  — permission:rider.tasks.update (status, OTP, photos, inspection)
///
/// NOTE: rider-self support-ticket routes are intentionally NOT migrated here — they
/// depend on the Orders Support sub-domain, which is outside this slice's scope.
/// </summary>
public class RiderSelfEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/rider";

    private const string TasksRead   = "permission:rider.tasks.read";
    private const string TasksUpdate = "permission:rider.tasks.update";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Rider Self-Service").RequireAuthorization("RiderOnly");

        // session lane (RiderOnly only)
        group.MapGet(GetMe, "/me");
        group.MapPatch(SetDuty, "/duty");
        group.MapPost(LocationPing, "/location/ping");
        group.MapPost(RegisterPushToken, "/push-token");
        group.MapDelete(DeactivatePushToken, "/push-token");

        // assignments
        group.MapGet(GetAssignmentsToday, "/assignments/today").RequireAuthorization(TasksRead);
        group.MapPatch(UpdateAssignmentStatus, "/assignments/{id:guid}/status").RequireAuthorization(TasksUpdate);
        group.MapPost(AcceptOffer, "/assignments/{id:guid}/accept").RequireAuthorization(TasksUpdate);
        group.MapPost(DeclineOffer, "/assignments/{id:guid}/decline").RequireAuthorization(TasksUpdate);

        // KYC documents (rider-self)
        group.MapGet(GetMyDocuments, "/documents").RequireAuthorization("RiderOnly");
        group.MapPost(UploadDocument, "/documents")
            .RequireAuthorization("RiderOnly")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024));

        // earnings balance + withdrawals + incentives
        group.MapGet(GetBalance, "/balance").RequireAuthorization(TasksRead);
        group.MapPost(RequestPayout, "/payout-requests").RequireAuthorization(TasksUpdate);
        group.MapGet(GetMyPayoutRequests, "/payout-requests").RequireAuthorization(TasksRead);
        group.MapGet(GetMyIncentives, "/incentives").RequireAuthorization(TasksRead);

        // per-order tasks (pickup/delivery legs)
        group.MapGet(GetTasksToday, "/tasks/today").RequireAuthorization(TasksRead);
        group.MapPatch(UpdateTaskStatus, "/tasks/{id:guid}/status").RequireAuthorization(TasksUpdate);
        group.MapPost(VerifyOtp, "/tasks/{id:guid}/verify-otp").RequireAuthorization(TasksUpdate);
        group.MapGet(GetTasksByDate, "/tasks").RequireAuthorization(TasksRead);

        group.MapPost(UploadProofPhoto, "/tasks/{id:guid}/proof-photo")
            .RequireAuthorization(TasksUpdate)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024)); // 10 MB + envelope

        group.MapPost(SubmitInspection, "/tasks/{id:guid}/inspection")
            .RequireAuthorization(TasksUpdate)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(22 * 1024 * 1024)); // 2 × 10 MB + overhead

        // earnings / payouts summary + COD self-summary
        group.MapGet(GetPayouts, "/payouts").RequireAuthorization(TasksRead);
    }

    // ── Session lane ────────────────────────────────────────────────────────────

    public static async Task<IResult> GetMe(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.QueryAsync(new GetMyRiderProfileQuery(userId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDto> { Status = true, Data = r });
    }

    public static async Task<IResult> SetDuty(SetDutyRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(new SetRiderDutyCommand(userId, brandId, req.OnDuty), ct);
        return result.Outcome switch
        {
            "ok" => Results.Ok(new SingleResponse<DutyToggleResponse> { Status = true, Data = result.Data }),
            _    => Results.NotFound()
        };
    }

    public static async Task<IResult> LocationPing(List<LocationPingInput> pings, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        if (pings is null || pings.Count == 0)
            return Results.BadRequest(new Response
            {
                Status  = false,
                Message = new Message { ResponseMessage = "Ping batch must not be empty." }
            });

        var result = await dispatcher.SendAsync(
            new BatchLocationPingCommand(userId, brandId, pings), ct);

        // 0 accepted = no rider profile for this user/brand → 404 (matches the legacy endpoint).
        return result.Accepted == 0 && pings.Count > 0
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PingBatchResponse> { Status = true, Data = result });
    }

    public static async Task<IResult> RegisterPushToken(
        RiderRegisterPushTokenRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        await dispatcher.SendAsync(
            new RegisterRiderPushTokenCommand(userId, brandId, req.Token, req.Platform), ct);
        return Results.Ok(new Response { Status = true });
    }

    public static async Task<IResult> DeactivatePushToken(
        [FromBody] RiderDeactivatePushTokenRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) return Results.Unauthorized();

        await dispatcher.SendAsync(new DeactivateRiderPushTokenCommand(userId, req.Token), ct);
        return Results.Ok(new Response { Status = true });
    }

    // ── Assignments ─────────────────────────────────────────────────────────────

    public static async Task<IResult> GetAssignmentsToday(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var list = await dispatcher.QueryAsync(new GetMyAssignmentsTodayQuery(userId, brandId), ct);
        return Results.Ok(new ListResponse<RiderAssignmentDto> { Status = true, Data = list });
    }

    public static async Task<IResult> UpdateAssignmentStatus(
        Guid id, RiderAssignmentStatusUpdateRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(
            new UpdateMyAssignmentStatusCommand(id, userId, brandId, req.Status), ct);
        return result is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = result });
    }

    public static async Task<IResult> AcceptOffer(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new AcceptOfferCommand(id, userId, brandId), ct);
        return r.Outcome switch
        {
            OfferActionOutcome.Ok       => Results.Ok(new SingleResponse<OfferActionResult> { Status = true, Data = r }),
            OfferActionOutcome.NotFound => Results.NotFound(),
            OfferActionOutcome.Expired  => Results.StatusCode(410),  // Gone — offer lapsed
            OfferActionOutcome.Taken    => Results.Conflict(new SingleResponse<OfferActionResult> { Status = false, Data = r }),
            _                            => Results.StatusCode(500),
        };
    }

    public static async Task<IResult> DeclineOffer(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new DeclineOfferCommand(id, userId, brandId), ct);
        return r.Outcome == OfferActionOutcome.NotFound
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OfferActionResult> { Status = true, Data = r });
    }

    // ── KYC documents ─────────────────────────────────────────────────────────────

    public static async Task<IResult> GetMyDocuments(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.QueryAsync(new GetMyRiderVerificationQuery(userId, brandId), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderVerificationView> { Status = true, Data = r });
    }

    public static async Task<IResult> UploadDocument(
        ICurrentUser u, [FromForm] string docType, IFormFile file, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new UploadRiderDocumentCommand(userId, brandId, docType, file), ct);
        return Results.Ok(new SingleResponse<RiderDocumentDto> { Status = true, Data = r });
    }

    // ── Earnings balance / withdrawals / incentives ───────────────────────────────

    public static async Task<IResult> GetBalance(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyBalanceQuery(userId, brandId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<RiderBalanceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> RequestPayout(RequestPayoutBody body, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.SendAsync(new RequestPayoutCommand(userId, brandId, body.Amount), ct);
        return Results.Ok(new SingleResponse<RiderPayoutRequestDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetMyPayoutRequests(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyPayoutRequestsQuery(userId, brandId), ct);
        return Results.Ok(new ListResponse<RiderPayoutRequestDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> GetMyIncentives(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct, int days = 30)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();
        var r = await dispatcher.QueryAsync(new GetMyIncentivesQuery(userId, brandId, days), ct);
        return Results.Ok(new ListResponse<RiderIncentiveAwardDto> { Status = true, Data = r.ToList() });
    }

    // ── Per-order tasks ───────────────────────────────────────────────────────────

    public static async Task<IResult> GetTasksToday(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var list = await dispatcher.QueryAsync(new GetMyTasksTodayQuery(userId, brandId), ct);
        return Results.Ok(new ListResponse<RiderTaskDto> { Status = true, Data = list });
    }

    public static async Task<IResult> UpdateTaskStatus(
        Guid id, RiderTaskStatusUpdateRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(
            new UpdateMyTaskStatusCommand(id, userId, brandId, req.Status, req.Reason, req.Note), ct);
        return ToTaskResult(result);
    }

    public static async Task<IResult> VerifyOtp(
        Guid id, RiderTaskOtpVerifyRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(new VerifyTaskOtpCommand(id, userId, brandId, req.Code), ct);
        return ToTaskResult(result);
    }

    public static async Task<IResult> GetTasksByDate(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct, string? date = null)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        // Require explicit date — this endpoint is the earnings drill-down, not a general list.
        if (!DateOnly.TryParse(date, out var parsedDate))
            return Results.BadRequest(new Response { Status = false,
                Message = new Message { ResponseMessage = "date query param is required in YYYY-MM-DD format." } });

        var list = await dispatcher.QueryAsync(new GetMyTasksByDateQuery(userId, brandId, parsedDate), ct);
        return Results.Ok(new ListResponse<RiderTaskDto> { Status = true, Data = list });
    }

    public static async Task<IResult> UploadProofPhoto(
        Guid id, IFormFile file, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.SendAsync(new UploadProofPhotoCommand(id, userId, brandId, file), ct);
        return ToTaskResult(result);
    }

    public static async Task<IResult> SubmitInspection(
        Guid id, HttpContext ctx, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        // Parse multipart fields manually — IFormFile injection doesn't work for named
        // optional files alongside non-file fields in minimal APIs.
        var form = ctx.Request.Form;

        var frontFile = form.Files["front"];
        if (frontFile is null)
            return Results.BadRequest(new Response { Status = false,
                Message = new Message { ResponseMessage = "front photo is required." } });

        var backFile = form.Files.GetFile("back"); // null when absent

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

        var result = await dispatcher.SendAsync(
            new SubmitPickupInspectionCommand(id, userId, brandId, frontFile, backFile, conditions, notes), ct);

        if (result.Outcome == "inspection_ok" && result.Error is not null)
        {
            var data = JsonSerializer.Deserialize<RiderInspectionResult>(result.Error);
            return Results.Ok(new SingleResponse<RiderInspectionResult> { Status = true, Data = data! });
        }

        return ToTaskResult(result);
    }

    // ── Earnings summary ──────────────────────────────────────────────────────────

    public static async Task<IResult> GetPayouts(ICurrentUser u, IDispatcher dispatcher, CancellationToken ct, int days = 30)
    {
        var userId = u.UserId ?? Guid.Empty; var brandId = u.BrandId ?? Guid.Empty;
        if (userId == Guid.Empty || brandId == Guid.Empty) return Results.Unauthorized();

        var result = await dispatcher.QueryAsync(new GetMyPayoutsQuery(userId, brandId, days), ct);
        return result is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderPayoutSummaryDto> { Status = true, Data = result });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Maps a RiderTaskResult onto an HTTP result: ok→200, conflict→400, else 404.</summary>
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
}

// ── Request DTOs (local to this endpoint group) ───────────────────────────────────

/// <param name="Token">Expo push token (ExponentPushToken[…] or ExpoPushToken[…]).</param>
/// <param name="Platform">"ios" or "android".</param>
public sealed record RiderRegisterPushTokenRequest(string Token, string Platform);

/// <param name="Token">The Expo push token to deactivate.</param>
public sealed record RiderDeactivatePushTokenRequest(string Token);
