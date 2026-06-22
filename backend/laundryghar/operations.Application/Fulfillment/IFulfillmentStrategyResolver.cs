namespace operations.Application.Fulfillment;

/// <summary>
/// Resolves the <see cref="IFulfillmentStrategy"/> for an order's vertical. An unknown or
/// null vertical falls back to laundry, so pre-migration rows (which have no/garbage
/// <c>vertical_key</c>) keep their existing behaviour.
/// </summary>
public interface IFulfillmentStrategyResolver
{
    IFulfillmentStrategy Resolve(string? verticalKey);
}
