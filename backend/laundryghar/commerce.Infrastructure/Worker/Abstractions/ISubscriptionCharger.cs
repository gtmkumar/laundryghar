using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

namespace commerce.Infrastructure.Worker.Abstractions;

/// <summary>
/// Seam for subscription mandate charging. Dev implementation simulates success.
/// Production: register a real gateway implementation via DI.
/// </summary>
public interface ISubscriptionCharger
{
    Task<SubscriptionChargeResult> ChargeAsync(
        CustomerSubscription sub,
        PaymentMandate?      mandate,
        SubscriptionInvoice  invoice,
        string               idempotencyKey,
        CancellationToken    ct);
}

public sealed record SubscriptionChargeResult(
    string  GatewayPaymentId,
    string  Status,           // "success" | "failed"
    string? FailureCode,
    string? FailureMessage);
