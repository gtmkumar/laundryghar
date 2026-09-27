using System.Diagnostics.CodeAnalysis;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// The resolved attribute values for one authorization decision — the output of the PIP.
///
/// The distinction that matters: a key that is ABSENT is unresolved (the resolver could not supply
/// it) and makes any comparison over it Indeterminate, which fails closed. A key that is PRESENT
/// with a null value is a known-null and compares normally. Collapsing those two into "null" is how
/// an authorization engine ends up fail-open, so they are kept apart here.
/// </summary>
public sealed class AbacAttributeBag
{
    private readonly Dictionary<string, object?> _values;

    public AbacAttributeBag(IDictionary<string, object?>? values = null)
        => _values = values is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the attribute was resolved (even to null).</summary>
    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

    public bool Has(string key) => _values.ContainsKey(key);

    public AbacAttributeBag Set(string key, object? value)
    {
        _values[key] = value;
        return this;
    }

    public IReadOnlyDictionary<string, object?> Values => _values;

    /// <summary>Resolved value or null when absent — for logging only, never for a decision.</summary>
    public object? this[string key] => _values.GetValueOrDefault(key);

    public static AbacAttributeBag Empty() => new();
}

/// <summary>Supplies a slice of the attribute bag. Keyed in DI by <see cref="Key"/> so a policy's
/// <c>authz.attribute.resolver_key</c> selects its provider.</summary>
public interface IAttributeResolver
{
    /// <summary>Matches authz.attribute.resolver_key — e.g. "subject", "environment".</summary>
    string Key { get; }

    /// <summary>Add every attribute this resolver owns to <paramref name="bag"/>. Must add a key
    /// only when it can genuinely resolve it: omitting a key fails closed, adding it with a wrong
    /// value does not.</summary>
    ValueTask ResolveAsync(AbacAttributeBag bag, CancellationToken ct);
}

/// <summary>A parsed scope node from the JWT scope_nodes claim: "platform" or "brand:&lt;uuid&gt;".</summary>
public readonly record struct AbacScopeNode(string ScopeType, Guid? ScopeId)
{
    public const string Platform = "platform";

    public static bool TryParse(string? raw, [NotNullWhen(true)] out AbacScopeNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var idx = raw.IndexOf(':');
        if (idx < 0)
        {
            node = new AbacScopeNode(raw.Trim(), null);
            return true;
        }

        var type = raw[..idx].Trim();
        if (type.Length == 0) return false;
        if (!Guid.TryParse(raw[(idx + 1)..], out var id)) return false;

        node = new AbacScopeNode(type, id);
        return true;
    }
}
