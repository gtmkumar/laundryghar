using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateEmailSettings;

public sealed record UpdateEmailSettingsCommand(UpdateEmailSettingsRequest Request) : ICommand<EmailSettingsView>;

public sealed class UpdateEmailSettingsHandler : ICommandHandler<UpdateEmailSettingsCommand, EmailSettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateEmailSettingsHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<EmailSettingsView> HandleAsync(UpdateEmailSettingsCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

        // Preserve the stored password when the client sends a blank one (it never receives the real value back).
        var existing = await SettingsStore.LoadEmailAsync(_db, brandId, ct);
        var password = string.IsNullOrEmpty(r.Password) ? existing.Password : r.Password;

        var value = new EmailSettings
        {
            Enabled = r.Enabled, Host = r.Host.Trim(), Port = r.Port, Secure = r.Secure,
            Username = r.Username.Trim(), Password = password,
            FromEmail = r.FromEmail.Trim(), FromName = string.IsNullOrWhiteSpace(r.FromName) ? "Laundry Ghar" : r.FromName.Trim(),
        };

        await SettingsStore.UpsertAsync(_db, brandId, "email", "smtp", value, isEncrypted: true, _user.UserId, ct);

        return new EmailSettingsView(
            value.Enabled, value.Host, value.Port, value.Secure,
            value.Username, PasswordSet: !string.IsNullOrEmpty(value.Password),
            value.FromEmail, value.FromName);
    }
}
