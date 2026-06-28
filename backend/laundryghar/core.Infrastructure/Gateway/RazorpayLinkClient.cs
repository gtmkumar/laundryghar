using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using core.Application.Common.Interfaces;
using core.Application.Identity.Settings;
using laundryghar.SharedDataModel.Crypto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Gateway;

/// <summary>
/// Razorpay Payment Links client (POST/GET /v1/payment_links). Basic-auth with the resolved
/// KeyId/KeySecret. Used to collect brand platform-tier (SaaS) invoices.
///
/// Credential resolution (settings-first, mirroring commerce's SettingsFirstPaymentGateway):
///   1. The platform-scoped <c>payment/platform_gateway</c> row (Settings → Platform billing) when
///      Enabled with a KeyId + KeySecret — the operator's dedicated SaaS-collection account.
///   2. Else env config Razorpay:KeyId / Razorpay:KeySecret (deployment secret / the gitignored CSV).
/// So the operator can manage the keys from the admin UI, run a separate SaaS account, or fall back to
/// a deployment env secret — with no key ever leaving the platform scope (RLS-bypassed reads only).
/// </summary>
public sealed class RazorpayLinkClient : IRazorpayLinkClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ICoreDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ILogger<RazorpayLinkClient> _logger;
    private readonly string _envKeyId;
    private readonly string _envKeySecret;

    // Memoized for the lifetime of this (scoped) instance so IsConfiguredAsync + a subsequent
    // Create/Get in the same request don't re-query the settings store.
    private (string KeyId, string KeySecret)? _resolved;

    public RazorpayLinkClient(
        IHttpClientFactory httpFactory, ICoreDbContext db, IFieldCipher cipher,
        IConfiguration config, ILogger<RazorpayLinkClient> logger)
    {
        _httpFactory  = httpFactory;
        _db           = db;
        _cipher       = cipher;
        _logger       = logger;
        _envKeyId     = config["Razorpay:KeyId"] ?? "";
        _envKeySecret = config["Razorpay:KeySecret"] ?? "";
    }

    /// <summary>Resolve credentials settings-first (platform_gateway row → env config). Memoized per scope.</summary>
    private async Task<(string KeyId, string KeySecret)> ResolveAsync(CancellationToken ct)
    {
        if (_resolved is { } cached) return cached;

        // 1. Dedicated platform-billing account from Settings → Platform billing (platform-scoped row).
        var s = await SettingsStore.LoadPlatformPaymentGatewayAsync(_db, _cipher, ct);
        if (s.Enabled && !string.IsNullOrWhiteSpace(s.KeyId) && !string.IsNullOrWhiteSpace(s.KeySecret))
        {
            _resolved = (s.KeyId!.Trim(), s.KeySecret!);
            return _resolved.Value;
        }

        // 2. Env/config fallback (deployment secret / gitignored CSV).
        _resolved = (_envKeyId, _envKeySecret);
        return _resolved.Value;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var (keyId, keySecret) = await ResolveAsync(ct);
        return !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(keySecret);
    }

    public async Task<RazorpayLink> CreatePaymentLinkAsync(
        decimal amount, string currency, string description, string referenceId,
        IReadOnlyDictionary<string, string>? notes = null, CancellationToken ct = default)
    {
        var (keyId, keySecret) = await ResolveAsync(ct);
        Ensure(keyId, keySecret);
        var body = new Dictionary<string, object?>
        {
            ["amount"] = (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero), // paise
            ["currency"] = currency.Trim().ToUpperInvariant(),
            ["description"] = description.Length > 2048 ? description[..2048] : description,
            ["reference_id"] = referenceId,
            ["reminder_enable"] = true,
        };
        if (notes is { Count: > 0 }) body["notes"] = notes;

        var http = Client(keyId, keySecret);
        var resp = await http.PostAsJsonAsync("v1/payment_links", body, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay CreatePaymentLink failed: {Status} {Body}", (int)resp.StatusCode, raw);
            throw new InvalidOperationException($"Razorpay payment-link create returned {(int)resp.StatusCode}: {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Razorpay link missing 'id'.");
        var shortUrl = root.TryGetProperty("short_url", out var u) ? u.GetString() ?? "" : "";
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "created" : "created";
        _logger.LogInformation("Razorpay payment link created {Id} ({Status}) for ref {Ref}", id, status, referenceId);
        return new RazorpayLink(id, shortUrl, status);
    }

    public async Task<string> GetPaymentLinkStatusAsync(string linkId, CancellationToken ct = default)
    {
        var (keyId, keySecret) = await ResolveAsync(ct);
        Ensure(keyId, keySecret);
        var http = Client(keyId, keySecret);
        var resp = await http.GetAsync($"v1/payment_links/{linkId}", ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Razorpay payment-link fetch returned {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "created" : "created";
    }

    private static void Ensure(string keyId, string keySecret)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            throw new InvalidOperationException(
                "Razorpay is not configured. Enable it under Settings → Platform billing, or set " +
                "Razorpay:KeyId / Razorpay:KeySecret (env: Razorpay__KeyId / Razorpay__KeySecret).");
    }

    private HttpClient Client(string keyId, string keySecret)
    {
        var http = _httpFactory.CreateClient("razorpay-core");
        if (http.BaseAddress is null) http.BaseAddress = new Uri("https://api.razorpay.com/");
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        return http;
    }
}
