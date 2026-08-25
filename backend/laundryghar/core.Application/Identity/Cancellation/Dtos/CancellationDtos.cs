namespace core.Application.Identity.Cancellation.Dtos;

/// <param name="RetentionDays">How long the data is kept before deletion. Optional; the handler's
/// default applies when omitted, and it is capped so nobody can set a window so long it is really
/// "never delete" or so short the export never happens.</param>
public sealed record RequestBrandCancellationRequest(string? Reason, int? RetentionDays);

public sealed record WithdrawBrandCancellationRequest(string? Reason);

/// <summary>What the owner sees during a wind-down. <paramref name="DaysRemaining"/> is the number
/// that matters — it is how long they have left to get their data out.</summary>
public sealed record BrandCancellationDto(
    Guid Id,
    Guid BrandId,
    string Status,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset RetentionUntil,
    int DaysRemaining,
    int ExportCount,
    DateTimeOffset? LastExportedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? PurgedAt);
