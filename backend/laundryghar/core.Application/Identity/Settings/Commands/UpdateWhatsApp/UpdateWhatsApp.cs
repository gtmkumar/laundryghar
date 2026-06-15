using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateWhatsApp;

public sealed record UpdateWhatsAppCommand(UpdateWhatsAppRequest Request) : ICommand<WhatsAppSettingsView>;

public sealed class UpdateWhatsAppHandler : ICommandHandler<UpdateWhatsAppCommand, WhatsAppSettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ICurrentUser _user;

    public UpdateWhatsAppHandler(ICoreDbContext db, IFieldCipher cipher, ICurrentUser user)
    {
        _db     = db;
        _cipher = cipher;
        _user   = user;
    }

    public async Task<WhatsAppSettingsView> HandleAsync(UpdateWhatsAppCommand command, CancellationToken ct)
    {
        var r       = command.Request;
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

        var existing    = await SettingsStore.LoadWhatsAppAsync(_db, brandId, _cipher, ct);
        var accessToken = string.IsNullOrEmpty(r.AccessToken) ? existing.AccessToken : r.AccessToken;

        var value = new WhatsAppSettings
        {
            Enabled         = r.Enabled,
            PhoneNumberId   = r.PhoneNumberId?.Trim(),
            AccessToken     = _cipher.Encrypt(accessToken),
            OtpEnabled      = r.OtpEnabled,
            OtpTemplateName = string.IsNullOrWhiteSpace(r.OtpTemplateName) ? null : r.OtpTemplateName.Trim(),
        };

        await SettingsStore.UpsertAsync(_db, brandId, "whatsapp", "cloud", value, isEncrypted: true, _user.UserId, ct);

        return new WhatsAppSettingsView(
            Enabled:         value.Enabled,
            PhoneNumberId:   value.PhoneNumberId,
            AccessTokenTail: SettingsStore.MaskSecret(accessToken),
            AccessTokenSet:  !string.IsNullOrEmpty(accessToken),
            OtpEnabled:      value.OtpEnabled,
            OtpTemplateName: value.OtpTemplateName);
    }
}
