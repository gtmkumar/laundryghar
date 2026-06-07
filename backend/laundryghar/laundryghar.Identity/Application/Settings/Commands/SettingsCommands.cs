using laundryghar.Identity.Application.Settings.Dtos;
using laundryghar.Identity.Infrastructure.Email;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using MediatR;

namespace laundryghar.Identity.Application.Settings.Commands;

// ── Update SMTP config ──────────────────────────────────────────────────────
public sealed record UpdateEmailSettingsCommand(UpdateEmailSettingsRequest Request, ICurrentUser User) : IRequest<EmailSettingsView>;

public sealed class UpdateEmailSettingsHandler : IRequestHandler<UpdateEmailSettingsCommand, EmailSettingsView>
{
    private readonly LaundryGharDbContext _db;
    public UpdateEmailSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<EmailSettingsView> Handle(UpdateEmailSettingsCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var brandId = await SettingsStore.ResolveBrandIdAsync(cmd.User, _db, ct);

        // Preserve the stored password when the client sends a blank one (it never receives the real value back).
        var existing = await SettingsStore.LoadEmailAsync(_db, brandId, ct);
        var password = string.IsNullOrEmpty(r.Password) ? existing.Password : r.Password;

        var value = new EmailSettings
        {
            Enabled = r.Enabled, Host = r.Host.Trim(), Port = r.Port, Secure = r.Secure,
            Username = r.Username.Trim(), Password = password,
            FromEmail = r.FromEmail.Trim(), FromName = string.IsNullOrWhiteSpace(r.FromName) ? "Laundry Ghar" : r.FromName.Trim(),
        };

        await SettingsStore.UpsertAsync(_db, brandId, "email", "smtp", value, isEncrypted: true, cmd.User.UserId, ct);

        return new EmailSettingsView(
            value.Enabled, value.Host, value.Port, value.Secure,
            value.Username, PasswordSet: !string.IsNullOrEmpty(value.Password),
            value.FromEmail, value.FromName);
    }
}

// ── Send a test email ───────────────────────────────────────────────────────
public sealed record TestEmailCommand(TestEmailRequest Request, ICurrentUser User) : IRequest<TestEmailResult>;

public sealed class TestEmailHandler : IRequestHandler<TestEmailCommand, TestEmailResult>
{
    private readonly LaundryGharDbContext _db;
    private readonly ISettingsMailer _mailer;
    public TestEmailHandler(LaundryGharDbContext db, ISettingsMailer mailer) { _db = db; _mailer = mailer; }

    public async Task<TestEmailResult> Handle(TestEmailCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Request.To))
            throw new ValidationException(new Dictionary<string, string[]> { ["to"] = ["A recipient address is required."] });

        var brandId = await SettingsStore.ResolveBrandIdAsync(cmd.User, _db, ct);
        var saved = await SettingsStore.LoadEmailAsync(_db, brandId, ct);

        // Use the unsaved form values if the UI supplied them, falling back to the
        // stored password when the form's password field was left blank.
        EmailSettings cfg = saved;
        if (cmd.Request.Settings is { } s)
        {
            cfg = new EmailSettings
            {
                Enabled = true, Host = s.Host.Trim(), Port = s.Port, Secure = s.Secure,
                Username = s.Username.Trim(),
                Password = string.IsNullOrEmpty(s.Password) ? saved.Password : s.Password,
                FromEmail = s.FromEmail.Trim(),
                FromName = string.IsNullOrWhiteSpace(s.FromName) ? "Laundry Ghar" : s.FromName.Trim(),
            };
        }

        var (ok, error) = await _mailer.TestAsync(cfg, cmd.Request.To.Trim(), ct);
        return new TestEmailResult(ok, error);
    }
}

// ── Update provisioning (invite) mode ───────────────────────────────────────
public sealed record UpdateProvisioningCommand(UpdateProvisioningRequest Request, ICurrentUser User) : IRequest<ProvisioningView>;

public sealed class UpdateProvisioningHandler : IRequestHandler<UpdateProvisioningCommand, ProvisioningView>
{
    private static readonly HashSet<string> Modes = new(StringComparer.OrdinalIgnoreCase) { "admin_activate", "self_service" };
    private readonly LaundryGharDbContext _db;
    public UpdateProvisioningHandler(LaundryGharDbContext db) => _db = db;

    public async Task<ProvisioningView> Handle(UpdateProvisioningCommand cmd, CancellationToken ct)
    {
        var mode = cmd.Request.Mode?.Trim().ToLowerInvariant() ?? "";
        if (!Modes.Contains(mode))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["mode"] = ["Mode must be 'admin_activate' or 'self_service'."] });

        var brandId = await SettingsStore.ResolveBrandIdAsync(cmd.User, _db, ct);
        await SettingsStore.UpsertAsync(_db, brandId, "provisioning", "invite", new { mode }, isEncrypted: false, cmd.User.UserId, ct);
        return new ProvisioningView(mode);
    }
}
