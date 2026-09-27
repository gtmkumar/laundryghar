namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Identifies the row an authorization question is about (A2.3).
///
/// <para><see cref="KnownAttributes"/> is the fast path: a handler that has already loaded the
/// entity passes its attributes directly and no second query happens. When it is null and
/// <see cref="ResourceId"/> is set, the resource resolver reads the row through
/// <c>authz.resource_type.attribute_map</c>.</para>
///
/// <para>A ref with neither known attributes nor an id is a COLLECTION question ("may I list
/// orders?"): no resource attributes are resolvable, so any policy that compares one evaluates
/// Indeterminate and fails closed. That is deliberate — list endpoints must go through
/// <see cref="IPolicyFilterBuilder"/> (A4.3), not through a per-row decision they cannot answer.</para>
/// </summary>
public sealed record AbacResourceRef(
    string ResourceType,
    Guid? ResourceId = null,
    IReadOnlyDictionary<string, object?>? KnownAttributes = null);

/// <summary>
/// The PIP facade (A2.1): assembles the attribute bag for one decision from the registered
/// resolvers. Scoped — the subject and environment slice is resolved once per request and reused
/// across every decision that request makes.
/// </summary>
public interface IAbacAttributeProvider
{
    ValueTask<AbacAttributeBag> BuildAsync(
        string resourceType, string action, AbacResourceRef? resource, CancellationToken ct);
}

/// <summary>
/// Reads a resource row's attributes when the caller supplied only an id. Separate from
/// <see cref="IAttributeResolver"/> because it is parameterised by the row under test, whereas
/// subject and environment resolvers depend only on the ambient request.
/// </summary>
public interface IResourceAttributeResolver
{
    ValueTask ResolveAsync(AbacAttributeBag bag, string resourceType, Guid resourceId, CancellationToken ct);
}

/// <inheritdoc cref="IAbacAttributeProvider"/>
public sealed class AbacAttributeProvider : IAbacAttributeProvider
{
    private readonly IReadOnlyList<IAttributeResolver> _contextResolvers;
    private readonly IResourceAttributeResolver? _resourceResolver;

    // Memoised per request (this service is scoped). Held as a Task so two concurrent decisions in
    // the same request share one resolution rather than racing two.
    private Task<AbacAttributeBag>? _contextSlice;

    public AbacAttributeProvider(
        IEnumerable<IAttributeResolver> contextResolvers,
        IResourceAttributeResolver? resourceResolver = null)
    {
        _contextResolvers = contextResolvers.ToList();
        _resourceResolver = resourceResolver;
    }

    public async ValueTask<AbacAttributeBag> BuildAsync(
        string resourceType, string action, AbacResourceRef? resource, CancellationToken ct)
    {
        var context = await (_contextSlice ??= ResolveContextAsync(ct));

        // Copy: a decision must never mutate the shared slice, or a resource attribute from one
        // decision would leak into the next one in the same request.
        var bag = new AbacAttributeBag(context.Values.ToDictionary(kv => kv.Key, kv => kv.Value));

        bag.Set(AbacAttributeKeys.ActionKey, action);
        bag.Set(AbacAttributeKeys.ResourceTypeKey, resourceType);

        if (resource is null) return bag;

        if (resource.ResourceId is { } id)
            bag.Set(AbacAttributeKeys.ResourceId, id);

        if (resource.KnownAttributes is { Count: > 0 })
        {
            foreach (var (key, value) in resource.KnownAttributes)
                bag.Set(key, value);
            return bag;
        }

        // Only reach for the database when the caller could not supply the row itself.
        if (_resourceResolver is not null && resource.ResourceId is { } rowId)
            await _resourceResolver.ResolveAsync(bag, resource.ResourceType, rowId, ct);

        return bag;
    }

    private async Task<AbacAttributeBag> ResolveContextAsync(CancellationToken ct)
    {
        var bag = AbacAttributeBag.Empty();
        foreach (var resolver in _contextResolvers)
            await resolver.ResolveAsync(bag, ct);
        return bag;
    }
}
