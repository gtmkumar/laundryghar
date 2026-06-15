namespace operations.Application.Logistics.RiderSelf.Dtos;

// ── Location ping inputs ───────────────────────────────────────────────────────

/// <summary>GPS ping submitted by a rider device.</summary>
public sealed record LocationPingInput(
    double Longitude,
    double Latitude,
    DateTimeOffset PingedAt,
    decimal? AccuracyMeters,
    decimal? SpeedKmph,
    decimal? HeadingDegrees,
    short?   BatteryPercent,
    bool?    IsMoving,
    string?  ActivityType,
    Guid?    CurrentAssignmentId);

/// <summary>Batch ping response.</summary>
public sealed record PingBatchResponse(int Accepted);

/// <summary>Rider-self status update for own assignment.</summary>
public sealed record RiderAssignmentStatusUpdateRequest(string Status);

/// <summary>
/// Rider-self status update for one per-order task (started/arrived/completed/failed).
/// For the 'failed' status, Reason and Note are optional metadata that are persisted on the assignment.
/// </summary>
/// <param name="Status">Target status: started | arrived | collected | completed | failed.</param>
/// <param name="Reason">Failure reason code (only for status=failed): customer_unavailable | address_issue | customer_refused | other.</param>
/// <param name="Note">Optional free-text note attached when status=failed.</param>
public sealed record RiderTaskStatusUpdateRequest(string Status, string? Reason = null, string? Note = null);

/// <summary>Code the customer reads out, submitted for server-side verification.</summary>
public sealed record RiderTaskOtpVerifyRequest(string Code);

// ── Duty toggle ────────────────────────────────────────────────────────────────

/// <summary>Request body for PATCH /api/v1/rider/duty.</summary>
public sealed record SetDutyRequest(bool OnDuty);

/// <summary>Response returned by the duty toggle endpoint.</summary>
public sealed record DutyToggleResponse(bool OnDuty, int OpenTaskCount);

// ── Rider task (per-order leg) view ─────────────────────────────────────────────

/// <summary>Mobile-facing view of one pickup/delivery/return leg.</summary>
public sealed record RiderTaskDto(
    Guid Id,
    string OrderNumber,
    string LegType,
    string Status,
    bool IsExpress,
    string CustomerName,
    string? CustomerPhone,
    string AddressLine,
    string? ZoneLabel,
    decimal? DistanceKm,
    int? EtaMinutes,
    string? ScheduledTime,
    int GarmentCount,
    decimal AmountDue,
    bool IsPaid,
    bool RequiresOtp,
    bool OtpVerified,
    decimal Payout,
    double? Lat,
    double? Lng,
    short? SequenceNumber,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? DroppedAt,
    string Phase);

/// <summary>Result of a status/OTP mutation. Outcome distinguishes the 404/409 cases for the endpoint.</summary>
public sealed record RiderTaskResult(string Outcome, RiderTaskDto? Task = null, string? Error = null)
{
    public static RiderTaskResult NotFound() => new("not_found");
    public static RiderTaskResult Conflict(string e) => new("conflict", Error: e);
    public static RiderTaskResult Ok(RiderTaskDto t) => new("ok", Task: t);
}

// ── KYC document + verification view ────────────────────────────────────────────

public sealed record RiderDocumentDto(
    Guid Id,
    string DocType,
    string FileName,
    string Status,
    string? RejectionReason,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset UploadedAt);

/// <summary>A rider's verification snapshot: identity (KYC) + vehicle gate + documents.</summary>
public sealed record RiderVerificationView(
    string KycStatus,
    string VehicleVerificationStatus,
    string? VehicleRejectionReason,
    IReadOnlyList<RiderDocumentDto> Documents);

// ── Earnings / payout / incentives ──────────────────────────────────────────────

/// <summary>A rider's withdrawable-balance breakdown.</summary>
public sealed record RiderBalanceDto(
    decimal EarnedPayout,
    decimal Incentives,
    decimal WithdrawnOrPending,
    decimal Available);

public sealed record RiderPayoutRequestDto(
    Guid Id,
    decimal Amount,
    string Status,
    string? RejectionReason,
    string? PaymentReference,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? PaidAt);

public sealed record RiderIncentiveAwardDto(
    Guid Id,
    string RuleName,
    string RuleType,
    decimal Amount,
    DateTimeOffset AwardedAt);

/// <summary>Rider-self withdrawal request body.</summary>
public sealed record RequestPayoutBody(decimal Amount);

/// <summary>One calendar-day earnings bucket.</summary>
public sealed record RiderPayoutDayDto(
    DateOnly Date,
    int TaskCount,
    decimal TotalPayout);

/// <summary>Earnings summary + per-day breakdown for the requested period.</summary>
public sealed record RiderPayoutSummaryDto(
    decimal TotalPayout,
    decimal AvgPerTask,
    int Days,
    IReadOnlyList<RiderPayoutDayDto> Breakdown);

// ── Pickup inspection ───────────────────────────────────────────────────────────

public sealed record InspectionConditions(bool Stains, bool Tears, bool MissingButtons);

/// <summary>
/// Intermediate deserialization target for the <c>conditions</c> JSON form field.
/// Property names are camelCase to match the rider-mobile ConditionFlags interface.
/// </summary>
public sealed class ConditionsFlagsInput
{
    public bool Stains         { get; set; }
    public bool Tears          { get; set; }
    public bool MissingButtons { get; set; }
}

public sealed record RiderInspectionResult(Guid InspectionId, Guid TaskId, DateTimeOffset RecordedAt);
