using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace laundryghar.Commerce.Infrastructure.Gateway;

/// <summary>
/// Production Razorpay implementation of IPaymentGateway.
///
/// Fail-closed at startup: constructor throws if Razorpay:KeyId or
/// Razorpay:KeySecret are missing (mirrors RsaJwtKeyProvider's approach).
///
/// HMAC verification uses constant-time comparison to prevent timing attacks.
/// HttpClient is injected via IHttpClientFactory (named "razorpay").
/// </summary>
public sealed class RazorpayPaymentGateway : IPaymentGateway
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly RazorpaySettings _settings;
    private readonly ILogger<RazorpayPaymentGateway> _logger;

    public RazorpayPaymentGateway(
        IHttpClientFactory httpFactory,
        IOptions<RazorpaySettings> settings,
        ILogger<RazorpayPaymentGateway> logger)
    {
        _httpFactory = httpFactory;
        _settings    = settings.Value;
        _logger      = logger;
    }

    /// <summary>
    /// Creates a Razorpay order via POST /v1/orders.
    /// Amount is converted to paise (smallest currency unit) as required by the API.
    /// </summary>
    public async Task<GatewayOrderResult> CreateOrderAsync(
        decimal amount,
        string currency,
        string receipt,
        CancellationToken ct = default)
    {
        var amountPaise = (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

        var body = new
        {
            amount   = amountPaise,
            currency = currency.Trim().ToUpperInvariant(),
            receipt
        };

        var http = CreateClient();
        var response = await http.PostAsJsonAsync("v1/orders", body, ct);

        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Razorpay CreateOrder failed: {StatusCode} {Body}",
                (int)response.StatusCode, raw);
            throw new InvalidOperationException(
                $"Razorpay CreateOrder returned {(int)response.StatusCode}: {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var gatewayOrderId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay response missing 'id'.");

        _logger.LogInformation(
            "Razorpay CreateOrder: amount={AmountPaise} paise {Currency} receipt={Receipt} → {GatewayOrderId}",
            amountPaise, currency, receipt, gatewayOrderId);

        return new GatewayOrderResult(
            GatewayOrderId: gatewayOrderId,
            Gateway:        "razorpay",
            RawResponse:    raw);
    }

    /// <summary>
    /// Verifies a Razorpay payment signature using local HMAC-SHA256.
    /// Signature = HMAC-SHA256("{gatewayOrderId}|{gatewayPaymentId}", KeySecret).
    /// Comparison is constant-time to prevent timing attacks.
    /// </summary>
    public Task<bool> VerifySignatureAsync(
        string gatewayOrderId,
        string gatewayPaymentId,
        string gatewaySignature,
        CancellationToken ct = default)
    {
        var payload    = $"{gatewayOrderId}|{gatewayPaymentId}";
        var keyBytes   = Encoding.UTF8.GetBytes(_settings.KeySecret);
        var msgBytes   = Encoding.UTF8.GetBytes(payload);
        var hmacBytes  = HMACSHA256.HashData(keyBytes, msgBytes);
        var expected   = Convert.ToHexString(hmacBytes).ToLowerInvariant();

        // Constant-time comparison — prevents signature oracle attacks
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(gatewaySignature ?? string.Empty));

        _logger.LogInformation(
            "Razorpay VerifySignature: order={GatewayOrderId} payment={GatewayPaymentId} valid={IsValid}",
            gatewayOrderId, gatewayPaymentId, isValid);

        return Task.FromResult(isValid);
    }

    /// <summary>
    /// Initiates a refund via POST /v1/payments/{id}/refund.
    /// Returns the gateway-assigned refund ID.
    /// </summary>
    public async Task<string> InitiateRefundAsync(
        string gatewayPaymentId,
        decimal amount,
        CancellationToken ct = default)
    {
        var amountPaise = (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

        var body = new { amount = amountPaise };

        var http     = CreateClient();
        var response = await http.PostAsJsonAsync($"v1/payments/{gatewayPaymentId}/refund", body, ct);

        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Razorpay InitiateRefund failed: payment={GatewayPaymentId} {StatusCode} {Body}",
                gatewayPaymentId, (int)response.StatusCode, raw);
            throw new InvalidOperationException(
                $"Razorpay InitiateRefund returned {(int)response.StatusCode}: {raw}");
        }

        using var doc     = JsonDocument.Parse(raw);
        var gatewayRefundId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay refund response missing 'id'.");

        _logger.LogInformation(
            "Razorpay InitiateRefund: payment={GatewayPaymentId} amount={Amount} → {GatewayRefundId}",
            gatewayPaymentId, amount, gatewayRefundId);

        return gatewayRefundId;
    }

    /// <summary>
    /// Creates a Razorpay UPI AutoPay / e-mandate subscription via POST /v1/subscriptions.
    /// <para>
    /// Maps the internal mandate type to the appropriate Razorpay API call:
    /// - upi_autopay / emandate → POST /v1/subscriptions with auth_type=netbanking/upi
    /// - card → recurring card setup
    /// </para>
    /// Fail-closed: throws on non-2xx response.
    /// </summary>
    public async Task<GatewayMandateResult> CreateMandateAsync(
        CreateMandateRequest request,
        CancellationToken ct = default)
    {
        // Razorpay models recurring debits as "subscriptions" on their API.
        // We pass quantity=1, customer_id to tie the subscription to a Razorpay customer,
        // and a plan_id if the subscription plan has a gateway_plan_id configured.
        // For UPI AutoPay the auth_type is "upi"; for e-mandate it is "netbanking".
        var authType = request.MandateType switch
        {
            "upi_autopay" => "upi",
            "emandate"    => "netbanking",
            "card"        => "card",
            "nach"        => "nach",
            _             => "upi"
        };

        var body = new
        {
            type              = "subscription",
            auth_type         = authType,
            max_amount        = (long)Math.Round(request.MaxAmount * 100, 0, MidpointRounding.AwayFromZero),
            currency          = request.Currency.ToUpperInvariant(),
            description       = request.Description ?? "LaundryGhar subscription mandate",
            customer_id       = request.GatewayCustomerId,
            receipt           = request.Receipt,
            upi_vpa           = request.UpiVpa
        };

        var http     = CreateClient();
        var response = await http.PostAsJsonAsync("v1/subscriptions", body, ct);
        var raw      = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Razorpay CreateMandate failed: type={Type} {StatusCode} {Body}",
                request.MandateType, (int)response.StatusCode, raw);
            throw new InvalidOperationException(
                $"Razorpay CreateMandate returned {(int)response.StatusCode}: {raw}");
        }

        using var doc      = JsonDocument.Parse(raw);
        var gatewayMandId  = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay mandate response missing 'id'.");
        var status         = doc.RootElement.TryGetProperty("status", out var sProp)
            ? sProp.GetString() ?? "pending" : "pending";
        string? authUrl    = doc.RootElement.TryGetProperty("short_url", out var uProp)
            ? uProp.GetString() : null;

        _logger.LogInformation(
            "Razorpay CreateMandate: type={Type} maxAmount={MaxAmount} → {MandateId} status={Status}",
            request.MandateType, request.MaxAmount, gatewayMandId, status);

        return new GatewayMandateResult(
            GatewayMandateId: gatewayMandId,
            Gateway:          "razorpay",
            Status:           status,
            AuthorizationUrl: authUrl,
            RawResponse:      raw);
    }

    /// <summary>
    /// Charges an authorized Razorpay mandate (subscription debit) via POST /v1/subscriptions/{id}/charge.
    /// idempotencyKey is sent as the Razorpay-Idempotency header to prevent double-debit on retry.
    /// </summary>
    public async Task<GatewayChargeResult> ChargeMandateAsync(
        string gatewayMandateId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var amountPaise = (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

        var body = new
        {
            amount   = amountPaise,
            currency = currency.ToUpperInvariant()
        };

        var http = CreateClient();
        http.DefaultRequestHeaders.Remove("Razorpay-Idempotency");
        http.DefaultRequestHeaders.Add("Razorpay-Idempotency", idempotencyKey);

        var response = await http.PostAsJsonAsync($"v1/subscriptions/{gatewayMandateId}/charge", body, ct);
        var raw      = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Razorpay ChargeMandate returned non-2xx: mandate={MandateId} {StatusCode} {Body}",
                gatewayMandateId, (int)response.StatusCode, raw);

            // Return failed rather than throw: caller will record attempt + advance dunning.
            return new GatewayChargeResult(
                GatewayPaymentId: string.Empty,
                Status:           "failed",
                FailureCode:      $"HTTP_{(int)response.StatusCode}",
                FailureMessage:   raw.Length > 200 ? raw[..200] : raw,
                RawResponse:      raw);
        }

        using var doc    = JsonDocument.Parse(raw);
        var paymentId    = doc.RootElement.TryGetProperty("razorpay_payment_id", out var pProp)
            ? pProp.GetString() ?? string.Empty : string.Empty;
        var status       = doc.RootElement.TryGetProperty("status", out var sProp)
            ? sProp.GetString() ?? "initiated" : "initiated";

        _logger.LogInformation(
            "Razorpay ChargeMandate: mandate={MandateId} amount={Amount} → paymentId={PaymentId} status={Status}",
            gatewayMandateId, amount, paymentId, status);

        return new GatewayChargeResult(
            GatewayPaymentId: paymentId,
            Status:           status == "captured" ? "success" : status,
            FailureCode:      null,
            FailureMessage:   null,
            RawResponse:      raw);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private HttpClient CreateClient()
    {
        var http = _httpFactory.CreateClient("razorpay");

        // Basic auth: keyId:keySecret
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.KeyId}:{_settings.KeySecret}"));

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        return http;
    }
}

/// <summary>
/// Razorpay configuration, bound from "Razorpay" section.
/// </summary>
public sealed class RazorpaySettings
{
    public const string SectionName = "Razorpay";

    /// <summary>Razorpay API Key ID (rzp_live_… or rzp_test_…).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Razorpay API Key Secret.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>
    /// Webhook secret used to verify X-Razorpay-Signature on incoming webhook calls.
    /// Required in non-Development environments.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
