using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Crypto;

namespace core.Application.Identity.Settings.Queries.GetPlatformPaymentGateway;

/// <summary>
/// Read the PLATFORM-scoped Razorpay account (the operator's SaaS-billing keys). Secrets are
/// returned masked (••••XXXX) + a "set" flag, never in clear. Platform-scoped read — only
/// resolvable under RLS bypass (platform admins).
/// </summary>
public sealed record GetPlatformPaymentGatewayQuery : IQuery<PaymentGatewaySettingsView>;

public sealed class GetPlatformPaymentGatewayHandler
    : IQueryHandler<GetPlatformPaymentGatewayQuery, PaymentGatewaySettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;

    public GetPlatformPaymentGatewayHandler(ICoreDbContext db, IFieldCipher cipher)
    {
        _db     = db;
        _cipher = cipher;
    }

    public async Task<PaymentGatewaySettingsView> HandleAsync(GetPlatformPaymentGatewayQuery query, CancellationToken ct)
    {
        var pgw = await SettingsStore.LoadPlatformPaymentGatewayAsync(_db, _cipher, ct);
        return new PaymentGatewaySettingsView(
            Provider:          pgw.Provider,
            Enabled:           pgw.Enabled,
            KeyId:             pgw.KeyId,
            KeySecretTail:     SettingsStore.MaskSecret(pgw.KeySecret),
            KeySecretSet:      !string.IsNullOrEmpty(pgw.KeySecret),
            WebhookSecretTail: SettingsStore.MaskSecret(pgw.WebhookSecret),
            WebhookSecretSet:  !string.IsNullOrEmpty(pgw.WebhookSecret),
            CodEnabled:        false);
    }
}
