using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace commerce.Infrastructure.Gateway;

/// <summary>
/// Razorpay Payment Links client for the RaaS partner-billing lane (POST/GET /v1/payment_links).
/// Byte-for-byte the same integration as core's <c>RazorpayLinkClient</c> (basic-auth, paise
/// conversion, settings-first credential resolution) — re-homed in the commerce BC because commerce
/// cannot reference core.Infrastructure and partner billing lives here.
///
/// Credential resolution (settings-first):
///   1. The platform-scoped <c>payment/platform_gateway</c> row (Settings → Platform billing) when
///      Enabled with a KeyId + KeySecret — the operator's dedicated SaaS/partner collection account.
///   2. Else env config <c>Razorpay:KeyId</c> / <c>Razorpay:KeySecret</c>.
///
/// NOTE on the settings read: kernel.system_settings has no RLS enabled, so the platform-scoped row
/// (brand_id IS NULL) is readable from an authenticated partner session (no brand claim) without a
/// bypass — same physical row core's paylink webhook reads under bypass.
/// </summary>
public sealed class PartnerRazorpayLinkClient : IPartnerPaymentLinkClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ICommerceDbContext _db;
    private readonly IFieldCipher _cipher;
    private readonly ILogger<PartnerRazorpayLinkClient> _logger;
    private readonly string _envKeyId;
    private readonly string _envKeySecret;

    // Memoized per (scoped) instance so IsConfiguredAsync + a Create/Get in the same request don't re-query.
    private (string KeyId, string KeySecret)? _resolved;

    public PartnerRazorpayLinkClient(
        IHttpClientFactory httpFactory, ICommerceDbContext db, IFieldCipher cipher,
        IConfiguration config, ILogger<PartnerRazorpayLinkClient> logger)
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

        var s = await LoadPlatformGatewayAsync(ct);
        if (s.Enabled && !string.IsNullOrWhiteSpace(s.KeyId) && !string.IsNullOrWhiteSpace(s.KeySecret))
        {
            _resolved = (s.KeyId!.Trim(), s.KeySecret!);
            return _resolved.Value;
        }

        _resolved = (_envKeyId, _envKeySecret);
        return _resolved.Value;
    }

    /// <summary>Reads the PLATFORM-scoped payment gateway row (category 'payment', key
    /// 'platform_gateway', brand_id IS NULL) and decrypts its secrets — mirrors core's
    /// <c>SettingsStore.LoadPlatformPaymentGatewayAsync</c>.</summary>
    private async Task<PaymentGatewaySettings> LoadPlatformGatewayAsync(CancellationToken ct)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "payment" && s.SettingKey == "platform_gateway"
                     && s.Status == "active" && s.BrandId == null)
            .FirstOrDefaultAsync(ct);
        return PaymentGatewaySettings.FromJson(row?.SettingValue, _cipher);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var (keyId, keySecret) = await ResolveAsync(ct);
        return !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(keySecret);
    }

    public async Task<PartnerPaymentLink> CreatePaymentLinkAsync(
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
            _logger.LogError("Razorpay (partner) CreatePaymentLink failed: {Status} {Body}", (int)resp.StatusCode, raw);
            throw new InvalidOperationException($"Razorpay payment-link create returned {(int)resp.StatusCode}: {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Razorpay link missing 'id'.");
        var shortUrl = root.TryGetProperty("short_url", out var u) ? u.GetString() ?? "" : "";
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "created" : "created";
        _logger.LogInformation("Razorpay (partner) payment link created {Id} ({Status}) for ref {Ref}", id, status, referenceId);
        return new PartnerPaymentLink(id, shortUrl, status);
    }

    public async Task<PartnerPaymentLinkDetails> GetPaymentLinkAsync(string linkId, CancellationToken ct = default)
    {
        var (keyId, keySecret) = await ResolveAsync(ct);
        Ensure(keyId, keySecret);
        var http = Client(keyId, keySecret);
        var resp = await http.GetAsync($"v1/payment_links/{linkId}", ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Razorpay payment-link fetch returned {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "created" : "created";
        // amount_paid is in paise; convert to major units.
        var paidPaise = root.TryGetProperty("amount_paid", out var ap) && ap.TryGetInt64(out var v) ? v : 0L;
        // notes are echoed back verbatim; pull-based reconcilers bind the credit to these server-set
        // values (partner_id / idempotency_key / kind), exactly like the push webhook does.
        return new PartnerPaymentLinkDetails(status, paidPaise / 100m, ReadNotes(root));
    }

    /// <summary>Reads the string-valued entries of the link's <c>notes</c> object (non-string values
    /// are skipped). Mirrors <c>ProcessPartnerPaylinkWebhookHandler.ReadNotes</c>.</summary>
    private static IReadOnlyDictionary<string, string> ReadNotes(JsonElement entity)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entity.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
            foreach (var p in notes.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    result[p.Name] = p.Value.GetString()!;
        return result;
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
        var http = _httpFactory.CreateClient("razorpay-partner");
        if (http.BaseAddress is null) http.BaseAddress = new Uri("https://api.razorpay.com/");
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        return http;
    }
}
