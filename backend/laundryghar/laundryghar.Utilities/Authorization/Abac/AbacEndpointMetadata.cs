using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Declares WHAT an endpoint touches (A4.1). Deliberately not who may call it: the whole point of
/// moving to policy rows is that the endpoint stops carrying the rule. An endpoint says
/// "this is a commerce.payment, and I am refunding it"; <c>authz.policy</c> says who may.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AbacResourceAttribute : Attribute
{
    public AbacResourceAttribute(string resourceType) => ResourceType = resourceType;
    public string ResourceType { get; }
}

/// <summary>Declares the action being attempted. Must be a key in <c>authz.action</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AbacActionAttribute : Attribute
{
    public AbacActionAttribute(string action) => Action = action;
    public string Action { get; }
}

/// <summary>
/// Names the route parameter carrying the resource's id, so the PEP can resolve the row's
/// attributes without the handler having loaded it. Omit it for collection endpoints — those are
/// filtered by <see cref="IPolicyFilterBuilder"/>, not gated per row.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AbacResourceIdAttribute : Attribute
{
    public AbacResourceIdAttribute(string routeParameterName) => RouteParameterName = routeParameterName;
    public string RouteParameterName { get; }
}

public static class AbacEndpointExtensions
{
    /// <summary>
    /// Tag a minimal-API endpoint with its ABAC resource and action.
    /// <paramref name="idRouteParameter"/> names the route value holding the row id.
    /// </summary>
    public static TBuilder RequireAbac<TBuilder>(
        this TBuilder builder, string resourceType, string action, string? idRouteParameter = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AbacResourceAttribute(resourceType));
        builder.WithMetadata(new AbacActionAttribute(action));
        if (idRouteParameter is not null)
            builder.WithMetadata(new AbacResourceIdAttribute(idRouteParameter));
        return builder;
    }

    /// <summary>Reads the ABAC target off the endpoint, or null when it declares none.</summary>
    public static AbacTarget? GetAbacTarget(this HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null) return null;

        var resource = endpoint.Metadata.GetMetadata<AbacResourceAttribute>();
        var action = endpoint.Metadata.GetMetadata<AbacActionAttribute>();
        if (resource is null || action is null) return null;

        Guid? id = null;
        var idParam = endpoint.Metadata.GetMetadata<AbacResourceIdAttribute>();
        if (idParam is not null
            && context.Request.RouteValues.TryGetValue(idParam.RouteParameterName, out var raw)
            && Guid.TryParse(raw?.ToString(), out var parsed))
            id = parsed;

        return new AbacTarget(resource.ResourceType, action.Action, id);
    }
}

/// <summary>What the endpoint says it is acting on.</summary>
public sealed record AbacTarget(string ResourceType, string Action, Guid? ResourceId);
