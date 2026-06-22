using laundryghar.SharedDataModel.Entities.OrderLifecycle;

namespace operations.Application.Fulfillment;

/// <summary>
/// Resolves the <see cref="IFulfillmentStrategy"/> for an order's fulfilment mode. An unknown or
/// null mode falls back to laundry's <c>process_deliver</c>, so pre-migration rows keep behaviour.
/// </summary>
public interface IFulfillmentStrategyResolver
{
    /// <summary>Resolve by an explicit <c>FulfillmentMode</c> value.</summary>
    IFulfillmentStrategy Resolve(string? fulfillmentMode);

    /// <summary>Resolve for an order: prefers its stored mode, falling back to the legacy
    /// <c>JobType</c> for any row not yet backfilled.</summary>
    IFulfillmentStrategy ResolveForOrder(Order order);
}
