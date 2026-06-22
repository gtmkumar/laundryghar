using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using EnumsNs = laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment;

/// <summary>
/// Default resolver — indexes the registered strategies by <see cref="IFulfillmentStrategy.FulfillmentMode"/>
/// and falls back to laundry's <c>process_deliver</c> for unknown/null modes (preserving existing rows).
/// </summary>
public sealed class FulfillmentStrategyResolver : IFulfillmentStrategyResolver
{
    private readonly IReadOnlyDictionary<string, IFulfillmentStrategy> _byMode;
    private readonly IFulfillmentStrategy _fallback;

    public FulfillmentStrategyResolver(IEnumerable<IFulfillmentStrategy> strategies)
    {
        _byMode = strategies.ToDictionary(s => s.FulfillmentMode, StringComparer.OrdinalIgnoreCase);

        if (_byMode.Count == 0)
            throw new InvalidOperationException("No IFulfillmentStrategy implementations registered.");

        _fallback = _byMode.TryGetValue(EnumsNs.FulfillmentMode.ProcessDeliver, out var laundry)
            ? laundry
            : _byMode.Values.First();
    }

    public IFulfillmentStrategy Resolve(string? fulfillmentMode)
        => fulfillmentMode is not null && _byMode.TryGetValue(fulfillmentMode, out var strategy)
            ? strategy
            : _fallback;

    public IFulfillmentStrategy ResolveForOrder(Order order)
    {
        // Prefer the stored mode; fall back to the legacy JobType for any not-yet-backfilled row,
        // exactly reproducing the former OrderStateMachine.MapFor(jobType) selection.
        var mode = !string.IsNullOrEmpty(order.FulfillmentMode)
            ? order.FulfillmentMode
            : (order.JobType == EnumsNs.JobType.Parcel
                ? EnumsNs.FulfillmentMode.PointToPoint
                : EnumsNs.FulfillmentMode.ProcessDeliver);

        return Resolve(mode);
    }
}
