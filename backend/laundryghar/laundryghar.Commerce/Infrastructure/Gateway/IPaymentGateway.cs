namespace laundryghar.Commerce.Infrastructure.Gateway;

/// <summary>
/// Abstraction over the external payment gateway (e.g. Razorpay).
/// Implementations may be swapped without touching business logic.
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
}

public sealed record GatewayOrderResult(
    string GatewayOrderId,
    string Gateway,
    string? RawResponse
);
