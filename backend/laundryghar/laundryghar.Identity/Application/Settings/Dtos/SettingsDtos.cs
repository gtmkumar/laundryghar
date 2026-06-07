namespace laundryghar.Identity.Application.Settings.Dtos;

// ── Read model ──────────────────────────────────────────────────────────────
/// <summary>SMTP config for the Settings UI. The password is never returned — only whether one is set.</summary>
public sealed record EmailSettingsView(
    bool Enabled, string Host, int Port, bool Secure,
    string Username, bool PasswordSet, string FromEmail, string FromName);

public sealed record ProvisioningView(string Mode);          // admin_activate | self_service
public sealed record AppUrlsView(string AdminBaseUrl);

public sealed record AdminSettingsView(
    EmailSettingsView Email, ProvisioningView Provisioning, AppUrlsView App);

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
