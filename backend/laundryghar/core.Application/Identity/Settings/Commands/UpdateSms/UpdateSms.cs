using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateSms;

public sealed record UpdateSmsCommand(UpdateSmsRequest Request) : ICommand<SmsSettingsView>;

public sealed class UpdateSmsHandler : ICommandHandler<UpdateSmsCommand, SmsSettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ICurrentUser _user;

    public UpdateSmsHandler(ICoreDbContext db, IFieldCipher cipher, ICurrentUser user)
    {
        _db     = db;
        _cipher = cipher;
        _user   = user;
    }

    public async Task<SmsSettingsView> HandleAsync(UpdateSmsCommand command, CancellationToken ct)
    {
        var r       = command.Request;
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

        var existing = await SettingsStore.LoadSmsAsync(_db, brandId, _cipher, ct);
        var authKey  = string.IsNullOrEmpty(r.AuthKey) ? existing.AuthKey : r.AuthKey;

        var value = new SmsSettings
        {
            Provider      = "msg91",
            Enabled       = r.Enabled,
            AuthKey       = _cipher.Encrypt(authKey),
            SenderId      = r.SenderId?.Trim(),
            DltTemplateId = r.DltTemplateId?.Trim(),
        };

        await SettingsStore.UpsertAsync(_db, brandId, "sms", "provider", value, isEncrypted: true, _user.UserId, ct);

        return new SmsSettingsView(
            Provider:      value.Provider,
            Enabled:       value.Enabled,
            AuthKeyTail:   SettingsStore.MaskSecret(authKey),
            AuthKeySet:    !string.IsNullOrEmpty(authKey),
            SenderId:      value.SenderId,
            DltTemplateId: value.DltTemplateId);
    }
}
