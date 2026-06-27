using System.Globalization;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace operations.Application.Fulfillment.Queries;

/// <summary>
/// Returns the client-consumable fulfilment configuration. With no mode → every registered mode
/// (so a client can mount the right pack); with a mode → just that one. Built live from the
/// registered <see cref="IFulfillmentStrategy"/> set, so adding a vertical (e.g. salon) makes its
/// stage descriptors available to clients with zero endpoint changes. (Phase 3.)
/// </summary>
public sealed record GetFulfillmentConfigQuery(string? FulfillmentMode = null)
    : IQuery<IReadOnlyList<FulfillmentConfigDto>>;

public sealed class GetFulfillmentConfigHandler
    : IQueryHandler<GetFulfillmentConfigQuery, IReadOnlyList<FulfillmentConfigDto>>
{
    private readonly IEnumerable<IFulfillmentStrategy> _strategies;

    public GetFulfillmentConfigHandler(IEnumerable<IFulfillmentStrategy> strategies)
        => _strategies = strategies;

    public Task<IReadOnlyList<FulfillmentConfigDto>> HandleAsync(GetFulfillmentConfigQuery q, CancellationToken ct)
    {
        var configs = _strategies
            .Where(s => q.FulfillmentMode is null
                     || string.Equals(s.FulfillmentMode, q.FulfillmentMode, StringComparison.OrdinalIgnoreCase))
            .Select(Describe)
            .ToList();

        return Task.FromResult<IReadOnlyList<FulfillmentConfigDto>>(configs);
    }

    private static FulfillmentConfigDto Describe(IFulfillmentStrategy s)
    {
        var path = s.GetHappyPath();
        var stages = path
            .Select((status, i) => new FulfillmentStageDto(status, Humanize(status), i, s.LifecycleStateFor(status)))
            .ToList();

        // Probe the mode's leg topology (each strategy decides given a "wants both" request).
        var legs = s.ResolveLegs(requestedPickup: true, requestedDelivery: true);

        return new FulfillmentConfigDto(
            s.FulfillmentMode,
            s.InitialStatus,
            stages,
            s.TerminalStatuses.ToList(),
            s.RequiresStoreDrop,
            legs.RequiresPickup,
            legs.RequiresDelivery);
    }

    /// <summary>"in_service" → "In Service".</summary>
    private static string Humanize(string status)
        => string.Join(' ', status.Split('_')
            .Select(w => w.Length == 0 ? w : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
}
