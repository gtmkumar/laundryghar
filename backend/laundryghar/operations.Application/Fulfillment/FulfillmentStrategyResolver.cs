using EnumsNs = laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment;

/// <summary>
/// Default resolver — indexes the registered strategies by <see cref="IFulfillmentStrategy.VerticalKey"/>
/// and falls back to the laundry strategy for unknown/null verticals (preserving existing rows).
/// </summary>
public sealed class FulfillmentStrategyResolver : IFulfillmentStrategyResolver
{
    private readonly IReadOnlyDictionary<string, IFulfillmentStrategy> _byVertical;
    private readonly IFulfillmentStrategy _fallback;

    public FulfillmentStrategyResolver(IEnumerable<IFulfillmentStrategy> strategies)
    {
        _byVertical = strategies.ToDictionary(s => s.VerticalKey, StringComparer.OrdinalIgnoreCase);

        if (_byVertical.Count == 0)
            throw new InvalidOperationException("No IFulfillmentStrategy implementations registered.");

        // Unknown vertical → laundry (the reference implementation / pre-migration default).
        _fallback = _byVertical.TryGetValue(EnumsNs.VerticalKey.Laundry, out var laundry)
            ? laundry
            : _byVertical.Values.First();
    }

    public IFulfillmentStrategy Resolve(string? verticalKey)
        => verticalKey is not null && _byVertical.TryGetValue(verticalKey, out var strategy)
            ? strategy
            : _fallback;
}
