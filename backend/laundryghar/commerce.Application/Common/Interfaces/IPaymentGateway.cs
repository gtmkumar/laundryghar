namespace commerce.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the external payment gateway (e.g. Razorpay).
/// Implementations may be swapped without touching business logic.
/// Lives in the Application layer so commerce handlers can inject it without taking a
/// dependency on commerce.Infrastructure (where the concrete gateways live).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates an order on the gateway and returns the gateway-assigned order ID.
    /// </summary>
    Task<GatewayOrderResult> CreateOrderAsync(
        decimal amount,
        string currency,
        string receipt,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a gateway payment signature.
    /// Returns true if signature is valid; false otherwise.
    /// </summary>
    Task<bool> VerifySignatureAsync(
        string gatewayOrderId,
        string gatewayPaymentId,
        string gatewaySignature,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a refund on the gateway.
    /// Returns gateway refund ID.
    /// </summary>
    Task<string> InitiateRefundAsync(
        string gatewayPaymentId,
        decimal amount,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a recurring mandate (UPI AutoPay / e-mandate) on the gateway.
    /// Returns the gateway mandate ID, its initial status, and an authorization
    /// URL the customer must visit to complete the setup flow.
    /// Fail-closed: throws on gateway error — caller stores attempt row first.
    /// </summary>
    Task<GatewayMandateResult> CreateMandateAsync(
        CreateMandateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Charges a previously authorized mandate for a recurring payment.
    /// Returns gateway payment ID and status.
    /// Idempotency is guaranteed via <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<GatewayChargeResult> ChargeMandateAsync(
        string gatewayMandateId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default);
}

public sealed record GatewayOrderResult(
    string GatewayOrderId,
    string Gateway,
    string? RawResponse
);

/// <summary>Input for mandate creation (UPI AutoPay / e-mandate).</summary>
public sealed record CreateMandateRequest(
    string MandateType,          // upi_autopay | emandate | card | nach
    string? GatewayCustomerId,
    decimal MaxAmount,
    string Currency,
    string DebitFrequency,
    string? UpiVpa,
    string? Receipt,
    string? Description
);

public sealed record GatewayMandateResult(
    string GatewayMandateId,
    string Gateway,
    string Status,               // created | pending
    string? AuthorizationUrl,
    string? RawResponse
);

public sealed record GatewayChargeResult(
    string GatewayPaymentId,
    string Status,               // initiated | success | failed
    string? FailureCode,
    string? FailureMessage,
    string? RawResponse
);
