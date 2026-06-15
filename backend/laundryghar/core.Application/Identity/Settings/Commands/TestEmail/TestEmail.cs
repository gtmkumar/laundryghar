using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.TestEmail;

public sealed record TestEmailCommand(TestEmailRequest Request) : ICommand<TestEmailResult>;

public sealed class TestEmailHandler : ICommandHandler<TestEmailCommand, TestEmailResult>
{
    private readonly ICoreDbContext _db;
    private readonly ISettingsMailer _mailer;
    private readonly ICurrentUser _user;

    public TestEmailHandler(ICoreDbContext db, ISettingsMailer mailer, ICurrentUser user)
    {
        _db     = db;
        _mailer = mailer;
        _user   = user;
    }

    public async Task<TestEmailResult> HandleAsync(TestEmailCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Request.To))
            throw new ValidationException(new Dictionary<string, string[]> { ["to"] = ["A recipient address is required."] });

        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);
        var saved = await SettingsStore.LoadEmailAsync(_db, brandId, ct);

        // Use the unsaved form values if the UI supplied them, falling back to the
        // stored password when the form's password field was left blank.
        EmailSettings cfg = saved;
        if (command.Request.Settings is { } s)
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

        var (ok, error) = await _mailer.TestAsync(cfg, command.Request.To.Trim(), ct);
        return new TestEmailResult(ok, error);
    }
}
