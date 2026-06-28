using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using core.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Gateway;

/// <summary>
/// Razorpay Payment Links client (POST/GET /v1/payment_links). Basic-auth with Razorpay:KeyId/KeySecret,
/// same pattern as commerce's RazorpayPaymentGateway. Used to collect brand platform-tier invoices.
/// </summary>
public sealed class RazorpayLinkClient : IRazorpayLinkClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RazorpayLinkClient> _logger;
    private readonly string _keyId;
    private readonly string _keySecret;

    public RazorpayLinkClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<RazorpayLinkClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _keyId = config["Razorpay:KeyId"] ?? "";
        _keySecret = config["Razorpay:KeySecret"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_keyId) && !string.IsNullOrWhiteSpace(_keySecret);

    public async Task<RazorpayLink> CreatePaymentLinkAsync(
        decimal amount, string currency, string description, string referenceId,
        IReadOnlyDictionary<string, string>? notes = null, CancellationToken ct = default)
    {
        Ensure();
        var body = new Dictionary<string, object?>
        {
            ["amount"] = (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero), // paise
            ["currency"] = currency.Trim().ToUpperInvariant(),
            ["description"] = description.Length > 2048 ? description[..2048] : description,
            ["reference_id"] = referenceId,
            ["reminder_enable"] = true,
        };
        if (notes is { Count: > 0 }) body["notes"] = notes;

        var http = Client();
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
        Ensure();
        var http = Client();
        var resp = await http.GetAsync($"v1/payment_links/{linkId}", ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Razorpay payment-link fetch returned {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "created" : "created";
    }

    private void Ensure()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Razorpay is not configured. Set Razorpay:KeyId and Razorpay:KeySecret (env: Razorpay__KeyId / Razorpay__KeySecret).");
    }

    private HttpClient Client()
    {
        var http = _httpFactory.CreateClient("razorpay-core");
        if (http.BaseAddress is null) http.BaseAddress = new Uri("https://api.razorpay.com/");
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_keyId}:{_keySecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        return http;
    }
}
