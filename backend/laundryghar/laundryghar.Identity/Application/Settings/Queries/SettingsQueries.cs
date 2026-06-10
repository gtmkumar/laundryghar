using laundryghar.Identity.Application.Settings.Dtos;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Persistence;
using MediatR;

namespace laundryghar.Identity.Application.Settings.Queries;

public sealed record GetAdminSettingsQuery(ICurrentUser User) : IRequest<AdminSettingsView>;

public sealed class GetAdminSettingsHandler : IRequestHandler<GetAdminSettingsQuery, AdminSettingsView>
{
    private readonly LaundryGharDbContext _db;
    private readonly IFieldCipher _cipher;

    public GetAdminSettingsHandler(LaundryGharDbContext db, IFieldCipher cipher)
    {
        _db     = db;
        _cipher = cipher;
    }

    public async Task<AdminSettingsView> Handle(GetAdminSettingsQuery q, CancellationToken ct)
    {
        var brandId = await SettingsStore.ResolveBrandIdAsync(q.User, _db, ct);

        var email   = await SettingsStore.LoadEmailAsync(_db, brandId, ct);
        var mode    = await SettingsStore.LoadProvisioningModeAsync(_db, brandId, ct);
        var baseUrl = await SettingsStore.LoadAdminBaseUrlAsync(_db, brandId, ct);
        var maps    = await SettingsStore.LoadMapsAsync(_db, brandId, ct);
        var payout  = await SettingsStore.LoadPayoutAsync(_db, brandId, ct);
        var pgw     = await SettingsStore.LoadPaymentGatewayAsync(_db, brandId, _cipher, ct);
        var wa      = await SettingsStore.LoadWhatsAppAsync(_db, brandId, _cipher, ct);
        var sms     = await SettingsStore.LoadSmsAsync(_db, brandId, _cipher, ct);

        return new AdminSettingsView(
            new EmailSettingsView(
                email.Enabled, email.Host, email.Port, email.Secure,
                email.Username, PasswordSet: !string.IsNullOrEmpty(email.Password),
                email.FromEmail, email.FromName),
            new ProvisioningView(mode),
            new AppUrlsView(baseUrl),
            new MapsSettingsView(maps.Provider, maps.GoogleApiKey, maps.MapboxToken),
            new PayoutSettingsView(payout.BaseFare, payout.PerKm, payout.ExpressBonus, payout.CodBonus, payout.RoundToNearest),
            new PaymentGatewaySettingsView(
                Provider:          pgw.Provider,
                Enabled:           pgw.Enabled,
                KeyId:             pgw.KeyId,
                KeySecretTail:     SettingsStore.MaskSecret(pgw.KeySecret),
                KeySecretSet:      !string.IsNullOrEmpty(pgw.KeySecret),
                WebhookSecretTail: SettingsStore.MaskSecret(pgw.WebhookSecret),
                WebhookSecretSet:  !string.IsNullOrEmpty(pgw.WebhookSecret),
                CodEnabled:        pgw.CodEnabled),
            new WhatsAppSettingsView(
                Enabled:         wa.Enabled,
                PhoneNumberId:   wa.PhoneNumberId,
                AccessTokenTail: SettingsStore.MaskSecret(wa.AccessToken),
                AccessTokenSet:  !string.IsNullOrEmpty(wa.AccessToken)),
            new SmsSettingsView(
                Provider:      sms.Provider,
                Enabled:       sms.Enabled,
                AuthKeyTail:   SettingsStore.MaskSecret(sms.AuthKey),
                AuthKeySet:    !string.IsNullOrEmpty(sms.AuthKey),
                SenderId:      sms.SenderId,
                DltTemplateId: sms.DltTemplateId));
    }
}
