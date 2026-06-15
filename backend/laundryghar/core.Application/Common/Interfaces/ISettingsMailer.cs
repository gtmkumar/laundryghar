using core.Application.Identity.Settings;

namespace core.Application.Common.Interfaces;

/// <summary>
/// Sends transactional email using the SMTP transport configured in
/// <c>kernel.system_settings</c> (brand-scoped). All sends are best-effort:
/// callers in the invite / activation flows must not fail their operation when
/// mail can't be delivered.
/// </summary>
public interface ISettingsMailer
{
    /// <summary>Loads the persisted SMTP config for a brand (null = first/only brand under RLS bypass).</summary>
    Task<EmailSettings?> LoadAsync(Guid? brandId, CancellationToken ct = default);

    /// <summary>Best-effort send using the persisted config. Returns false (and logs) if unconfigured/disabled/failed.</summary>
    Task<bool> SendAsync(Guid? brandId, string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>Sends using an explicit config (possibly unsaved) and surfaces the error — used by "Send test email".</summary>
    Task<(bool ok, string? error)> TestAsync(EmailSettings cfg, string to, CancellationToken ct = default);
}
