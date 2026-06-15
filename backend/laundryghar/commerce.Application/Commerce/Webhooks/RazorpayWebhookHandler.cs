using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Kernel;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace commerce.Application.Commerce.Webhooks;

// ── Command ────────────────────────────────────────────────────────────────────

/// <summary>
/// Processes an inbound Razorpay webhook payload.
/// Raw body bytes are carried so the HMAC can be re-verified inside the handler
/// without depending on re-serialization.
/// </summary>
public sealed record ProcessRazorpayWebhookCommand(
    byte[] RawBody,
    string? Signature
) : ICommand<WebhookProcessResult>;

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
    : ICommandHandler<ProcessRazorpayWebhookCommand, WebhookProcessResult>
{
    private readonly ICommerceDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly IGatewaySettingsCache? _cache;
    private readonly IFieldCipher? _cipher;
    private readonly ILogger<ProcessRazorpayWebhookHandler> _logger;

    public ProcessRazorpayWebhookHandler(
        ICommerceDbContext db,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<ProcessRazorpayWebhookHandler> logger,
        IGatewaySettingsCache? cache = null,
        IFieldCipher? cipher = null)
    {
        _db     = db;
        _config = config;
        _env    = env;
        _cache  = cache;
        _cipher = cipher;
        _logger = logger;
    }

    public async Task<WebhookProcessResult> HandleAsync(
        ProcessRazorpayWebhookCommand cmd, CancellationToken ct)
    {
        // ── 1. Parse event (UNTRUSTED — not yet verified) ─────────────────────
        // We must parse before we can verify, because the webhook secret is per-brand and
        // the brand is only knowable from the payment row referenced by the parsed entity.
        // Nothing is persisted/acted upon until HMAC passes in step 3.
        WebhookEvent? evt;
        try
        {
            evt = ParseEvent(cmd.RawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Razorpay webhook payload parse error.");
            return new WebhookProcessResult(false, "Malformed JSON payload.");
        }

        if (evt is null)
            return new WebhookProcessResult(false, "Empty payload.");

        var gatewayOrderId = evt.Payload?.Payment?.Entity?.OrderId;

        // ── 2. Resolve the matched payment + its brand ────────────────────────
        // bypass_rls is set for this anonymous path so we must filter manually by gateway id.
        // (No RLS brand predicate is available on an anonymous webhook.)
        Payment? payment = null;
        if (!string.IsNullOrEmpty(gatewayOrderId))
        {
            payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.GatewayOrderId == gatewayOrderId, ct);
        }

        // ── 3. HMAC verification, scoped to the matched payment's brand (SEC-2) ─
        // Resolution order: DB settings for the PAYMENT's brand → env config →
        // skip in Development / reject in Production. We never prime the secret from a
        // process-global cache populated by an unrelated caller.
        string? webhookSecret = null;
        if (_cache is not null && _cipher is not null)
        {
            var dbSettings = await _cache.GetAsync(_db, payment?.BrandId, ct);
            if (!string.IsNullOrWhiteSpace(dbSettings.WebhookSecret))
                webhookSecret = dbSettings.WebhookSecret;
        }

        webhookSecret ??= _config["Razorpay:WebhookSecret"];

        // Fail-closed policy (applies in ALL environments):
        //   • Secret configured  → signature MUST be present and valid, else reject.
        //   • Secret NOT configured + non-Development → reject (never trust unsigned in prod/staging).
        //   • Secret NOT configured + Development:
        //       - if an X-Razorpay-Signature header IS present, we cannot verify it →
        //         reject (an unverifiable signature is treated as hostile, not skipped).
        //       - if NO signature header is present, accept-unsigned so the dev gateway
        //         (DevPaymentGateway) can drive the flow locally — logged loudly.
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            if (!_env.IsDevelopment())
                return new WebhookProcessResult(false,
                    "Razorpay:WebhookSecret is not configured. Webhook rejected.");

            if (!string.IsNullOrEmpty(cmd.Signature))
            {
                _logger.LogWarning(
                    "[DEV] Razorpay:WebhookSecret not set but X-Razorpay-Signature header present — "
                    + "cannot verify, rejecting fail-closed.");
                return new WebhookProcessResult(false, "Invalid signature.");
            }

            _logger.LogWarning(
                "[DEV] Razorpay:WebhookSecret not set and no signature header — accepting UNSIGNED "
                + "webhook (Development only; never reachable in non-Development).");
        }
        else
        {
            if (!VerifyHmac(cmd.RawBody, cmd.Signature, webhookSecret))
            {
                _logger.LogWarning("Razorpay webhook HMAC verification failed.");
                return new WebhookProcessResult(false, "Invalid signature.");
            }
        }

        // ── 4. Act on the verified event ──────────────────────────────────────

        _logger.LogInformation(
            "Razorpay webhook event={EventType} entity={EntityId}",
            evt.Event, evt.Payload?.Payment?.Entity?.Id);

        return evt.Event switch
        {
            "payment.captured" => await HandleCapturedAsync(evt, payment, ct),
            "payment.failed"   => await HandleFailedAsync(evt, payment, ct),
            _                  => new WebhookProcessResult(true, $"Event '{evt.Event}' ignored.")
        };
    }

    // ── payment.captured ──────────────────────────────────────────────────────

    private async Task<WebhookProcessResult> HandleCapturedAsync(
        WebhookEvent evt, Payment? payment, CancellationToken ct)
    {
        var entity = evt.Payload?.Payment?.Entity;
        if (entity is null)
            return new WebhookProcessResult(false, "Missing payment entity.");

        var gatewayOrderId   = entity.OrderId;
        var gatewayPaymentId = entity.Id;

        if (string.IsNullOrEmpty(gatewayOrderId) || string.IsNullOrEmpty(gatewayPaymentId))
            return new WebhookProcessResult(false, "Missing gateway order/payment id.");

        // `payment` was resolved + brand-scoped for HMAC in Handle(); reuse it.
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
        WebhookEvent evt, Payment? payment, CancellationToken ct)
    {
        var entity = evt.Payload?.Payment?.Entity;
        if (entity is null)
            return new WebhookProcessResult(false, "Missing payment entity.");

        var gatewayOrderId   = entity.OrderId;
        var gatewayPaymentId = entity.Id;

        if (string.IsNullOrEmpty(gatewayOrderId))
            return new WebhookProcessResult(false, "Missing gateway order id.");

        // `payment` was resolved + brand-scoped for HMAC in Handle(); reuse it.
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

    // Real Razorpay payloads are snake_case (order_id, error_code). Dev gateway and
    // existing fixtures may send camelCase (orderId, errorCode). We parse with the
    // snake_case policy first, then fill any nulls from a camelCase parse so BOTH
    // shapes bind. PropertyNameCaseInsensitive on each pass tolerates casing drift.
    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CamelOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // internal for white-box unit testing of the dual snake_case/camelCase parse.
    internal static WebhookEvent? ParseEvent(byte[] rawBody)
    {
        var snake = JsonSerializer.Deserialize<WebhookEvent>(rawBody, SnakeOptions);
        var camel = JsonSerializer.Deserialize<WebhookEvent>(rawBody, CamelOptions);

        if (snake is null) return camel;
        if (camel is null) return snake;

        // Merge: prefer snake_case (real gateway), fall back to camelCase per field.
        snake.Event ??= camel.Event;

        var snakeEntity = snake.Payload?.Payment?.Entity;
        var camelEntity = camel.Payload?.Payment?.Entity;
        if (camelEntity is not null)
        {
            if (snakeEntity is null)
            {
                snake.Payload ??= new WebhookPayload();
                snake.Payload.Payment ??= new WebhookPaymentWrapper();
                snake.Payload.Payment.Entity = camelEntity;
            }
            else
            {
                snakeEntity.Id               ??= camelEntity.Id;
                snakeEntity.OrderId          ??= camelEntity.OrderId;
                snakeEntity.ErrorCode        ??= camelEntity.ErrorCode;
                snakeEntity.ErrorDescription ??= camelEntity.ErrorDescription;
            }
        }

        return snake;
    }

    // internal for white-box unit testing of bad-signature rejection.
    internal static bool VerifyHmac(byte[] body, string? signature, string secret)
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

    internal sealed class WebhookEvent
    {
        public string? Event   { get; set; }
        public WebhookPayload? Payload { get; set; }
    }

    internal sealed class WebhookPayload
    {
        public WebhookPaymentWrapper? Payment { get; set; }
    }

    internal sealed class WebhookPaymentWrapper
    {
        public WebhookPaymentEntity? Entity { get; set; }
    }

    internal sealed class WebhookPaymentEntity
    {
        public string? Id               { get; set; }
        public string? OrderId          { get; set; }
        public string? ErrorCode        { get; set; }
        public string? ErrorDescription { get; set; }
    }
}
