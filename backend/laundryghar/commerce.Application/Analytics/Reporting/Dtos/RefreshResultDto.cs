namespace commerce.Application.Analytics.Reporting.Dtos;

/// <summary>
/// Result of the analytics matview refresh. <see cref="Refreshed"/> is false and
/// <see cref="Error"/> carries the message when the SECURITY DEFINER refresh function fails;
/// the endpoint still returns 200 with an envelope (Status=false) — preserving legacy behaviour.
/// </summary>
public sealed record RefreshResultDto(bool Refreshed, string? Error = null);
