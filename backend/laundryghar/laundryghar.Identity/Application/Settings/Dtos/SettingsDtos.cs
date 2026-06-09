namespace laundryghar.Identity.Application.Settings.Dtos;

// ── Read model ──────────────────────────────────────────────────────────────
/// <summary>SMTP config for the Settings UI. The password is never returned — only whether one is set.</summary>
public sealed record EmailSettingsView(
    bool Enabled, string Host, int Port, bool Secure,
    string Username, bool PasswordSet, string FromEmail, string FromName);

public sealed record ProvisioningView(string Mode);          // admin_activate | self_service
public sealed record AppUrlsView(string AdminBaseUrl);

/// <summary>
/// Map-provider config for the admin live map. Provider is osm | google | mapbox.
/// The keys ARE returned (unlike the SMTP password) because map SDK keys are used
/// in the browser and are client-exposed by design — the client needs them to render.
/// </summary>
public sealed record MapsSettingsView(string Provider, string? GoogleApiKey, string? MapboxToken);

/// <summary>
/// Rider per-leg payout rates (₹). payout = base + perKm·km + (express? expressBonus)
/// + (cod? codBonus), rounded to the nearest <c>RoundToNearest</c>.
/// </summary>
public sealed record PayoutSettingsView(
    decimal BaseFare, decimal PerKm, decimal ExpressBonus, decimal CodBonus, decimal RoundToNearest);

public sealed record AdminSettingsView(
    EmailSettingsView Email, ProvisioningView Provisioning, AppUrlsView App,
    MapsSettingsView Maps, PayoutSettingsView Payout);

// ── Write models ────────────────────────────────────────────────────────────
/// <summary>
/// Update SMTP config. <c>Password</c> is optional: when null/empty the stored
/// password is preserved (so the UI never has to re-enter it on every save).
/// </summary>
public sealed record UpdateEmailSettingsRequest(
    bool Enabled, string Host, int Port, bool Secure,
    string Username, string? Password, string FromEmail, string FromName);

/// <summary>Send a test email. If <c>Settings</c> is provided it is used as-is (unsaved); otherwise the saved config is used.</summary>
public sealed record TestEmailRequest(string To, UpdateEmailSettingsRequest? Settings);

public sealed record TestEmailResult(bool Sent, string? Error);

public sealed record UpdateProvisioningRequest(string Mode);

/// <summary>
/// Update map provider config. Keys are optional: a null/blank key preserves the
/// stored one (so the UI need not re-enter a key on every save, like SMTP).
/// </summary>
public sealed record UpdateMapsSettingsRequest(string Provider, string? GoogleApiKey, string? MapboxToken);

/// <summary>Update rider payout rates (all ₹; non-negative; RoundToNearest must be &gt; 0).</summary>
public sealed record UpdatePayoutSettingsRequest(
    decimal BaseFare, decimal PerKm, decimal ExpressBonus, decimal CodBonus, decimal RoundToNearest);
