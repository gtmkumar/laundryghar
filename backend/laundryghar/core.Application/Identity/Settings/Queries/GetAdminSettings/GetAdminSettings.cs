using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Queries.GetAdminSettings;

public sealed record GetAdminSettingsQuery : IQuery<AdminSettingsView>;

public sealed class GetAdminSettingsHandler : IQueryHandler<GetAdminSettingsQuery, AdminSettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ICurrentUser _user;

    public GetAdminSettingsHandler(ICoreDbContext db, IFieldCipher cipher, ICurrentUser user)
    {
        _db     = db;
        _cipher = cipher;
        _user   = user;
    }

    public async Task<AdminSettingsView> HandleAsync(GetAdminSettingsQuery query, CancellationToken ct)
    {
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

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
                AccessTokenSet:  !string.IsNullOrEmpty(wa.AccessToken),
                OtpEnabled:      wa.OtpEnabled,
                OtpTemplateName: wa.OtpTemplateName),
            new SmsSettingsView(
                Provider:      sms.Provider,
                Enabled:       sms.Enabled,
                AuthKeyTail:   SettingsStore.MaskSecret(sms.AuthKey),
                AuthKeySet:    !string.IsNullOrEmpty(sms.AuthKey),
                SenderId:      sms.SenderId,
                DltTemplateId: sms.DltTemplateId));
    }
}
