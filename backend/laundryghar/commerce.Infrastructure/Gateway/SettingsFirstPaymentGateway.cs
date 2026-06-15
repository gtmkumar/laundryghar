using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Gateway;

/// <summary>
/// Settings-first <see cref="IPaymentGateway"/> wrapper.
///
/// Resolution order:
///   1. If <c>kernel.system_settings</c> has a payment/gateway row with
///      <c>Enabled=true</c>, <c>KeyId</c>, and <c>KeySecret</c> → use those credentials.
///   2. Otherwise fall back to the env-config <see cref="RazorpaySettings"/> (set at startup
///      from appsettings / environment variables / Key Vault).
///   3. If neither source has credentials → throw <see cref="InvalidOperationException"/>
///      (fail-closed; prevents silent no-op payments).
///
/// Development security posture is PRESERVED:
///   In Development, the registered <see cref="IPaymentGateway"/> is <see cref="DevPaymentGateway"/>
///   — this wrapper is never reached because Program.cs short-circuits to DevPaymentGateway.
///   The wrapper exists only for non-Development environments.
///
/// Registration: registered as Scoped (not Singleton) so each request gets a fresh
/// <c>ICommerceDbContext</c> scope. The <see cref="GatewaySettingsCache"/> is
/// Singleton — it holds the TTL cache across requests.
/// </summary>
public sealed class SettingsFirstPaymentGateway : IPaymentGateway
{
    private readonly GatewaySettingsCache _cache;
    private readonly ICommerceDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly RazorpaySettings _envSettings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SettingsFirstPaymentGateway> _logger;

    public SettingsFirstPaymentGateway(
        GatewaySettingsCache cache,
        ICommerceDbContext db,
        ICurrentTenant tenant,
        IOptions<RazorpaySettings> envSettings,
        IHttpClientFactory httpFactory,
        ILogger<SettingsFirstPaymentGateway> logger)
    {
        _cache       = cache;
        _db          = db;
        _tenant      = tenant;
        _envSettings = envSettings.Value;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // ── Public IPaymentGateway surface ────────────────────────────────────────

    public async Task<GatewayOrderResult> CreateOrderAsync(
        decimal amount, string currency, string receipt, CancellationToken ct = default)
        => await (await ResolveAsync(ct)).CreateOrderAsync(amount, currency, receipt, ct);

    public async Task<bool> VerifySignatureAsync(
        string gatewayOrderId, string gatewayPaymentId, string gatewaySignature, CancellationToken ct = default)
        => await (await ResolveAsync(ct)).VerifySignatureAsync(gatewayOrderId, gatewayPaymentId, gatewaySignature, ct);

    public async Task<string> InitiateRefundAsync(
        string gatewayPaymentId, decimal amount, CancellationToken ct = default)
        => await (await ResolveAsync(ct)).InitiateRefundAsync(gatewayPaymentId, amount, ct);

    public async Task<GatewayMandateResult> CreateMandateAsync(
        CreateMandateRequest request, CancellationToken ct = default)
        => await (await ResolveAsync(ct)).CreateMandateAsync(request, ct);

    public async Task<GatewayChargeResult> ChargeMandateAsync(
        string gatewayMandateId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
        => await (await ResolveAsync(ct)).ChargeMandateAsync(gatewayMandateId, amount, currency, idempotencyKey, ct);

    // ── Resolution logic ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns a configured <see cref="RazorpayPaymentGateway"/> using either
    /// DB-loaded credentials (TTL cache) or the env-config fallback.
    /// Fail-closed: throws when neither source provides credentials.
    /// </summary>
    private async Task<RazorpayPaymentGateway> ResolveAsync(CancellationToken ct)
    {
        // 1. Try DB settings (TTL-cached, keyed by the CURRENT brand so one brand's creds
        //    are never served to another — SEC-2). _tenant.BrandId is the authenticated
        //    caller's brand on the customer/admin payment lanes.
        var dbSettings = await _cache.GetAsync(_db, _tenant.BrandId, ct);

        if (dbSettings.Enabled
            && !string.IsNullOrWhiteSpace(dbSettings.KeyId)
            && !string.IsNullOrWhiteSpace(dbSettings.KeySecret))
        {
            _logger.LogDebug("PaymentGateway: resolved from DB settings (settings row).");
            return Build(dbSettings.KeyId!, dbSettings.KeySecret!, dbSettings.WebhookSecret);
        }

        // 2. Fall back to env config
        if (!string.IsNullOrWhiteSpace(_envSettings.KeyId)
            && !string.IsNullOrWhiteSpace(_envSettings.KeySecret))
        {
            _logger.LogDebug("PaymentGateway: resolved from env configuration.");
            return Build(_envSettings.KeyId, _envSettings.KeySecret, _envSettings.WebhookSecret);
        }

        // 3. Neither source configured — fail closed
        throw new InvalidOperationException(
            "Payment gateway is not configured. " +
            "Provide credentials via the admin Settings → Payments panel or " +
            "via Razorpay:KeyId / Razorpay:KeySecret environment variables.");
    }

    private RazorpayPaymentGateway Build(string keyId, string keySecret, string? webhookSecret)
    {
        var settings = new RazorpaySettings
        {
            KeyId         = keyId,
            KeySecret     = keySecret,
            WebhookSecret = webhookSecret,
        };
        return new RazorpayPaymentGateway(
            _httpFactory,
            Options.Create(settings),
            _logger as ILogger<RazorpayPaymentGateway>
                ?? NullLogger<RazorpayPaymentGateway>.Instance);
    }
}
