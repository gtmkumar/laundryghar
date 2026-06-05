namespace laundryghar.Commerce.Infrastructure.Gateway;

/// <summary>
/// Development stub for IPaymentGateway.
/// CreateOrder returns a fake gateway_order_id; VerifySignature always returns true.
/// NEVER register this in non-Development environments.
/// </summary>
public sealed class DevPaymentGateway : IPaymentGateway
{
    private readonly ILogger<DevPaymentGateway> _logger;

    public DevPaymentGateway(ILogger<DevPaymentGateway> logger) => _logger = logger;

    public Task<GatewayOrderResult> CreateOrderAsync(
        decimal amount,
        string currency,
        string receipt,
        CancellationToken ct = default)
    {
        var gatewayOrderId = $"dev_order_{Guid.NewGuid():N}";
        _logger.LogInformation(
            "[DEV] CreateOrder: amount={Amount} {Currency} receipt={Receipt} → {GatewayOrderId}",
            amount, currency, receipt, gatewayOrderId);

        return Task.FromResult(new GatewayOrderResult(
            GatewayOrderId: gatewayOrderId,
            Gateway: "dev",
            RawResponse: $"{{\"id\":\"{gatewayOrderId}\",\"amount\":{amount},\"currency\":\"{currency}\"}}"));
    }

    public Task<bool> VerifySignatureAsync(
        string gatewayOrderId,
        string gatewayPaymentId,
        string gatewaySignature,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DEV] VerifySignature: order={GatewayOrderId} payment={GatewayPaymentId} → true (stub)",
            gatewayOrderId, gatewayPaymentId);

        // Stub: signature verification always succeeds in Development.
        return Task.FromResult(true);
    }

    public Task<string> InitiateRefundAsync(
        string gatewayPaymentId,
        decimal amount,
        CancellationToken ct = default)
    {
        var refundId = $"dev_refund_{Guid.NewGuid():N}";
        _logger.LogInformation(
            "[DEV] InitiateRefund: payment={GatewayPaymentId} amount={Amount} → {RefundId}",
            gatewayPaymentId, amount, refundId);

        return Task.FromResult(refundId);
    }
}
