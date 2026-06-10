using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using MediatR;

namespace laundryghar.Commerce.Application.Webhooks;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Processes an inbound Razorpay webhook payload.
/// Raw body bytes are carried so the HMAC can be re-verified inside the handler
/// without depending on re-serialization.
/// </summary>
public sealed record ProcessRazorpayWebhookCommand(
    byte[] RawBody,
    string? Signature
) : IRequest<WebhookProcessResult>;

public sealed record WebhookProcessResult(bool Accepted, string Reason);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles Razorpay webhooks for payment.captured and payment.failed events.
///
/// Security: verifies X-Razorpay-Signature = HMAC-SHA256(raw_body, WebhookSecret)
/// before parsing. Fails closed if WebhookSecret is unset in non-Development.
///
/// RLS: runs unauthenticated — HttpContext.Items["bypass_rls"] must be set by
/// the endpoint before this handler executes, following the same pattern as
/// Identity's anonymous auth paths.
///
/// Idempotency: payment already captured → 200 no-op (not an error).
/// </summary>
public sealed class ProcessRazorpayWebhookHandler
    : IRequestHandler<ProcessRazorpayWebhookCommand, WebhookProcessResult>
{
    private readonly LaundryGharDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly GatewaySettingsCache? _cache;
    private readonly IFieldCipher? _cipher;
    private readonly ILogger<ProcessRazorpayWebhookHandler> _logger;

    public ProcessRazorpayWebhookHandler(
        LaundryGharDbContext db,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<ProcessRazorpayWebhookHandler> logger,
        GatewaySettingsCache? cache = null,
        IFieldCipher? cipher = null)
    {
        _db     = db;
        _config = config;
        _env    = env;
        _cache  = cache;
        _cipher = cipher;
        _logger = logger;
    }

    public async Task<WebhookProcessResult> Handle(
        ProcessRazorpayWebhookCommand cmd, CancellationToken ct)
    {
        // ── 1. HMAC verification ──────────────────────────────────────────────

        // Resolution order: DB settings → env config → skip in Development / reject in Production.
        string? webhookSecret = null;
        if (_cache is not null && _cipher is not null)
        {
            var dbSettings = await _cache.GetAsync(_db, ct);
            if (!string.IsNullOrWhiteSpace(dbSettings.WebhookSecret))
                webhookSecret = dbSettings.WebhookSecret;
        }

        webhookSecret ??= _config["Razorpay:WebhookSecret"];

        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            if (!_env.IsDevelopment())
                return new WebhookProcessResult(false,
                    "Razorpay:WebhookSecret is not configured. Webhook rejected.");

            // In Development: log the skip so it's visible, but allow through.
            _logger.LogWarning("[DEV] Razorpay:WebhookSecret not set — skipping HMAC verification.");
        }
        else
        {
            if (!VerifyHmac(cmd.RawBody, cmd.Signature, webhookSecret))
            {
                _logger.LogWarning("Razorpay webhook HMAC verification failed.");
                return new WebhookProcessResult(false, "Invalid signature.");
            }
        }

        // ── 2. Parse event ────────────────────────────────────────────────────

        WebhookEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<WebhookEvent>(
                cmd.RawBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Razorpay webhook payload parse error.");
            return new WebhookProcessResult(false, "Malformed JSON payload.");
        }

        if (evt is null)
            return new WebhookProcessResult(false, "Empty payload.");

        _logger.LogInformation(
            "Razorpay webhook event={EventType} entity={EntityId}",
            evt.Event, evt.Payload?.Payment?.Entity?.Id);

        return evt.Event switch
        {
            "payment.captured" => await HandleCapturedAsync(evt, ct),
            "payment.failed"   => await HandleFailedAsync(evt, ct),
            _                  => new WebhookProcessResult(true, $"Event '{evt.Event}' ignored.")
        };
    }

    // ── payment.captured ──────────────────────────────────────────────────────

    private async Task<WebhookProcessResult> HandleCapturedAsync(
        WebhookEvent evt, CancellationToken ct)
    {
        var entity = evt.Payload?.Payment?.Entity;
        if (entity is null)
            return new WebhookProcessResult(false, "Missing payment entity.");

        var gatewayOrderId   = entity.OrderId;
        var gatewayPaymentId = entity.Id;

        if (string.IsNullOrEmpty(gatewayOrderId) || string.IsNullOrEmpty(gatewayPaymentId))
            return new WebhookProcessResult(false, "Missing gateway order/payment id.");

        // Explicit filter by gateway ids (bypass_rls is true for this path so we must
        // filter manually — no RLS brand predicate available on anonymous webhooks).
        var payment = await _db.Payments
            .FirstOrDefaultAsync(
                p => p.GatewayOrderId == gatewayOrderId, ct);

        if (payment is null)
        {
            _logger.LogWarning(
                "Razorpay webhook payment.captured: no payment found for order={GatewayOrderId}",
                gatewayOrderId);
            // Return 200: Razorpay retries on non-2xx — avoid infinite retries for unknown orders.
            return new WebhookProcessResult(true, "Payment record not found — acknowledged.");
        }

        // Idempotency: already captured → no-op
        if (payment.Status == "captured" || payment.Status == "completed")
        {
            _logger.LogInformation(
                "Razorpay webhook payment.captured: payment {PaymentId} already {Status} — no-op.",
                payment.Id, payment.Status);
            return new WebhookProcessResult(true, "Already captured — no-op.");
        }

        if (payment.Status != "pending")
        {
            _logger.LogWarning(
                "Razorpay webhook payment.captured: payment {PaymentId} in status '{Status}' — skipped.",
                payment.Id, payment.Status);
            return new WebhookProcessResult(true, $"Unexpected status '{payment.Status}' — acknowledged.");
        }

        var now = DateTimeOffset.UtcNow;

        payment.Status           = "captured";
        payment.GatewayPaymentId = gatewayPaymentId;
        payment.CompletedAt      = now;
        payment.UpdatedAt        = now;

        // Outbox event (same pattern as VerifyPaymentHandler)
        var outbox = BuildOutboxEvent(
            brandId:     payment.BrandId,
            aggregateId: payment.Id,
            eventType:   "payment.captured",
            payload:     JsonSerializer.Serialize(new
            {
                paymentId        = payment.Id,
                brandId          = payment.BrandId,
                gatewayOrderId,
                gatewayPaymentId,
                amount           = payment.Amount,
                currencyCode     = payment.CurrencyCode,
                capturedAt       = now,
                source           = "webhook"
            }),
            now: now);

        _db.OutboxEvents.Add(outbox);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Razorpay webhook payment.captured: payment {PaymentId} marked captured.",
            payment.Id);

        return new WebhookProcessResult(true, "Captured.");
    }

    // ── payment.failed ────────────────────────────────────────────────────────

    private async Task<WebhookProcessResult> HandleFailedAsync(
        WebhookEvent evt, CancellationToken ct)
    {
        var entity = evt.Payload?.Payment?.Entity;
        if (entity is null)
            return new WebhookProcessResult(false, "Missing payment entity.");

        var gatewayOrderId   = entity.OrderId;
        var gatewayPaymentId = entity.Id;

        if (string.IsNullOrEmpty(gatewayOrderId))
            return new WebhookProcessResult(false, "Missing gateway order id.");

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.GatewayOrderId == gatewayOrderId, ct);

        if (payment is null)
        {
            _logger.LogWarning(
                "Razorpay webhook payment.failed: no payment found for order={GatewayOrderId}",
                gatewayOrderId);
            return new WebhookProcessResult(true, "Payment record not found — acknowledged.");
        }

        // Already in a terminal state — no-op
        if (payment.Status is "failed" or "captured" or "completed")
        {
            _logger.LogInformation(
                "Razorpay webhook payment.failed: payment {PaymentId} already {Status} — no-op.",
                payment.Id, payment.Status);
            return new WebhookProcessResult(true, $"Already in status '{payment.Status}' — no-op.");
        }

        var now  = DateTimeOffset.UtcNow;
        var errDesc = entity.ErrorDescription;

        payment.Status         = "failed";
        payment.FailedAt       = now;
        payment.FailureCode    = entity.ErrorCode ?? "GATEWAY_FAILED";
        payment.FailureMessage = errDesc;
        payment.UpdatedAt      = now;

        var outbox = BuildOutboxEvent(
            brandId:     payment.BrandId,
            aggregateId: payment.Id,
            eventType:   "payment.failed",
            payload:     JsonSerializer.Serialize(new
            {
                paymentId        = payment.Id,
                brandId          = payment.BrandId,
                gatewayOrderId,
                gatewayPaymentId,
                failureCode      = payment.FailureCode,
                failureMessage   = payment.FailureMessage,
                failedAt         = now,
                source           = "webhook"
            }),
            now: now);

        _db.OutboxEvents.Add(outbox);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Razorpay webhook payment.failed: payment {PaymentId} marked failed.",
            payment.Id);

        return new WebhookProcessResult(true, "Failed.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool VerifyHmac(byte[] body, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        var keyBytes  = Encoding.UTF8.GetBytes(secret);
        var hmacBytes = HMACSHA256.HashData(keyBytes, body);
        var expected  = Convert.ToHexString(hmacBytes).ToLowerInvariant();

        // Constant-time comparison
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    private static OutboxEvent BuildOutboxEvent(
        Guid brandId, Guid aggregateId, string eventType, string payload, DateTimeOffset now)
        => new()
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "payment",
            AggregateId   = aggregateId,
            EventType     = eventType,
            EventVersion  = 1,
            Payload       = payload,
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now
        };

    // ── Razorpay webhook payload model (minimal projection) ───────────────────

    private sealed class WebhookEvent
    {
        public string? Event   { get; set; }
        public WebhookPayload? Payload { get; set; }
    }

    private sealed class WebhookPayload
    {
        public WebhookPaymentWrapper? Payment { get; set; }
    }

    private sealed class WebhookPaymentWrapper
    {
        public WebhookPaymentEntity? Entity { get; set; }
    }

    private sealed class WebhookPaymentEntity
    {
        public string? Id               { get; set; }
        public string? OrderId          { get; set; }
        public string? ErrorCode        { get; set; }
        public string? ErrorDescription { get; set; }
    }
}
