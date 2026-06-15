using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdatePaymentGateway;

public sealed record UpdatePaymentGatewayCommand(UpdatePaymentGatewayRequest Request) : ICommand<PaymentGatewaySettingsView>;

public sealed class UpdatePaymentGatewayHandler : ICommandHandler<UpdatePaymentGatewayCommand, PaymentGatewaySettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ICurrentUser _user;

    public UpdatePaymentGatewayHandler(ICoreDbContext db, IFieldCipher cipher, ICurrentUser user)
    {
        _db     = db;
        _cipher = cipher;
        _user   = user;
    }

    public async Task<PaymentGatewaySettingsView> HandleAsync(UpdatePaymentGatewayCommand command, CancellationToken ct)
    {
        var r       = command.Request;
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

        // Preserve stored secrets when the client sends blank (SMTP pattern).
        var existing    = await SettingsStore.LoadPaymentGatewayAsync(_db, brandId, _cipher, ct);
        var keySecret    = string.IsNullOrEmpty(r.KeySecret)    ? existing.KeySecret    : r.KeySecret;
        var webhookSecret = string.IsNullOrEmpty(r.WebhookSecret) ? existing.WebhookSecret : r.WebhookSecret;

        // Persist secrets encrypted; plaintext stays in memory only.
        var value = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = r.Enabled,
            KeyId         = r.KeyId?.Trim(),
            KeySecret     = _cipher.Encrypt(keySecret),
            WebhookSecret = _cipher.Encrypt(webhookSecret),
            CodEnabled    = r.CodEnabled,
        };

        await SettingsStore.UpsertAsync(_db, brandId, "payment", "gateway", value, isEncrypted: true, _user.UserId, ct);

        return new PaymentGatewaySettingsView(
            Provider:          value.Provider,
            Enabled:           value.Enabled,
            KeyId:             value.KeyId,
            KeySecretTail:     SettingsStore.MaskSecret(keySecret),
            KeySecretSet:      !string.IsNullOrEmpty(keySecret),
            WebhookSecretTail: SettingsStore.MaskSecret(webhookSecret),
            WebhookSecretSet:  !string.IsNullOrEmpty(webhookSecret),
            CodEnabled:        value.CodEnabled);
    }
}
