using commerce.Application.Common.Interfaces;
using commerce.Infrastructure.Worker.Abstractions;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace commerce.Infrastructure.Worker;

/// <summary>
/// Production <see cref="ISubscriptionCharger"/>: charges the customer's authorized mandate through the
/// real <see cref="IPaymentGateway"/> (Razorpay subscription debit). Registered for non-Development;
/// the dev stub (<see cref="DevSubscriptionCharger"/>) is used in Development.
///
/// Singleton-safe: <see cref="IPaymentGateway"/> is scoped, so it is resolved from a fresh scope per
/// charge (the charger is injected into the singleton SubscriptionBillingService).
/// </summary>
public sealed class GatewaySubscriptionCharger : ISubscriptionCharger
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GatewaySubscriptionCharger> _logger;

    public GatewaySubscriptionCharger(IServiceScopeFactory scopes, ILogger<GatewaySubscriptionCharger> logger)
    { _scopes = scopes; _logger = logger; }

    public async Task<SubscriptionChargeResult> ChargeAsync(
        CustomerSubscription sub, PaymentMandate? mandate, SubscriptionInvoice invoice,
        string idempotencyKey, CancellationToken ct)
    {
        if (mandate?.GatewayMandateId is not { Length: > 0 } mandateId)
            return new SubscriptionChargeResult("", "failed", "no_mandate", "No active payment mandate to charge.");

        await using var scope = _scopes.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();

        var r = await gateway.ChargeMandateAsync(mandateId, invoice.GrandTotal, invoice.CurrencyCode, idempotencyKey, ct);
        var status = r.Status == "success" ? "success" : r.Status == "failed" ? "failed" : r.Status;
        _logger.LogInformation("GatewaySubscriptionCharger: invoice {InvId} mandate {Mandate} → {Status}", invoice.Id, mandateId, status);
        return new SubscriptionChargeResult(r.GatewayPaymentId, status, r.FailureCode, r.FailureMessage);
    }
}
