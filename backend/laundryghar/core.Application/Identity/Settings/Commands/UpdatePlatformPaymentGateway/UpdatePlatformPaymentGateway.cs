using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdatePlatformPaymentGateway;

/// <summary>
/// Upsert the PLATFORM-scoped Razorpay account (category 'payment', key 'platform_gateway',
/// BrandId == null) used to collect SaaS tier invoices from tenant brands. Secrets are encrypted
/// (AES-256-GCM via <see cref="IFieldCipher"/>); a blank secret preserves the stored one (SMTP pattern).
/// Platform-scoped write — only reachable by platform admins (RLS bypass).
/// </summary>
public sealed record UpdatePlatformPaymentGatewayCommand(UpdatePlatformPaymentGatewayRequest Request)
    : ICommand<PaymentGatewaySettingsView>;

public sealed class UpdatePlatformPaymentGatewayHandler
    : ICommandHandler<UpdatePlatformPaymentGatewayCommand, PaymentGatewaySettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ICurrentUser _user;

    public UpdatePlatformPaymentGatewayHandler(ICoreDbContext db, IFieldCipher cipher, ICurrentUser user)
    {
        _db     = db;
        _cipher = cipher;
        _user   = user;
    }

    public async Task<PaymentGatewaySettingsView> HandleAsync(UpdatePlatformPaymentGatewayCommand command, CancellationToken ct)
    {
        var r = command.Request;

        // Preserve stored secrets when the client sends blank (SMTP pattern).
        var existing      = await SettingsStore.LoadPlatformPaymentGatewayAsync(_db, _cipher, ct);
        var keySecret     = string.IsNullOrEmpty(r.KeySecret)     ? existing.KeySecret     : r.KeySecret;
        var webhookSecret = string.IsNullOrEmpty(r.WebhookSecret) ? existing.WebhookSecret : r.WebhookSecret;

        var value = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = r.Enabled,
            KeyId         = r.KeyId?.Trim(),
            KeySecret     = _cipher.Encrypt(keySecret),
            WebhookSecret = _cipher.Encrypt(webhookSecret),
            CodEnabled    = false, // N/A for B2B platform billing
        };

        // Platform scope (brandId == null) — mirrors UpdateDispatchSettings.
        await SettingsStore.UpsertAsync(_db, brandId: null, "payment", "platform_gateway", value, isEncrypted: true, _user.UserId, ct);

        return new PaymentGatewaySettingsView(
            Provider:          value.Provider,
            Enabled:           value.Enabled,
            KeyId:             value.KeyId,
            KeySecretTail:     SettingsStore.MaskSecret(keySecret),
            KeySecretSet:      !string.IsNullOrEmpty(keySecret),
            WebhookSecretTail: SettingsStore.MaskSecret(webhookSecret),
            WebhookSecretSet:  !string.IsNullOrEmpty(webhookSecret),
            CodEnabled:        false);
    }
}
