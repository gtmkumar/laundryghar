using commerce.Infrastructure.Worker.Abstractions;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.Extensions.Logging;

namespace commerce.Infrastructure.Worker;

/// <summary>
/// Development-only stub: always returns success so the billing cycle can be
/// exercised without a live gateway. MUST NOT be registered in Production.
/// </summary>
public sealed class DevSubscriptionCharger : ISubscriptionCharger
{
    private readonly ILogger<DevSubscriptionCharger> _logger;

    public DevSubscriptionCharger(ILogger<DevSubscriptionCharger> logger) => _logger = logger;

    public Task<SubscriptionChargeResult> ChargeAsync(
        CustomerSubscription sub,
        PaymentMandate?      mandate,
        SubscriptionInvoice  invoice,
        string               idempotencyKey,
        CancellationToken    ct)
    {
        _logger.LogDebug(
            "[DevStub] Simulating subscription charge: invoiceId={InvId} mandateId={MandateId} key={Key}",
            invoice.Id, mandate?.Id, idempotencyKey);

        return Task.FromResult(new SubscriptionChargeResult(
            GatewayPaymentId: $"dev_sim_{idempotencyKey[..Math.Min(16, idempotencyKey.Length)]}",
            Status:           "success",
            FailureCode:      null,
            FailureMessage:   null));
    }
}
