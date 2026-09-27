using System.Collections;
using System.Globalization;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Evaluates a policy's condition tree against a resolved attribute bag (A3.1).
///
/// Pure and dependency-free on purpose: this is the half of the ABAC engine whose correctness is
/// worth a unit-test matrix, and it must be reproducible by authz.permits() in SQL (A5.1). Anything
/// that needs a database, a clock or an HTTP context belongs in a resolver, not here.
///
/// Three-valued throughout. An unresolved attribute yields Indeterminate rather than false, so the
/// PDP can fail closed on a DENY it could not evaluate instead of quietly dropping it.
/// </summary>
public static class ConditionEvaluator
{
    public static Ternary Evaluate(AbacCondition? condition, AbacAttributeBag attrs)
    {
        // No condition = unconditional. This is what a plain role_permissions row projects to.
        if (condition is null) return Ternary.True;

        return condition.NodeType switch
        {
            ConditionNodeType.And     => EvaluateAnd(condition, attrs),
            ConditionNodeType.Or      => EvaluateOr(condition, attrs),
            ConditionNodeType.Not     => Negate(EvaluateSingleChild(condition, attrs)),
            ConditionNodeType.Compare => EvaluateCompare(condition, attrs),
            _                         => Ternary.Indeterminate, // unknown node type fails closed
        };
    }

    private static Ternary EvaluateAnd(AbacCondition node, AbacAttributeBag attrs)
    {
        if (node.Children.Count == 0) return Ternary.True; // empty AND is vacuously true

        var sawIndeterminate = false;
        foreach (var child in node.Children)
        {
            switch (Evaluate(child, attrs))
            {
                case Ternary.False: return Ternary.False;   // one false settles an AND
                case Ternary.Indeterminate: sawIndeterminate = true; break;
            }
        }
        return sawIndeterminate ? Ternary.Indeterminate : Ternary.True;
    }

    private static Ternary EvaluateOr(AbacCondition node, AbacAttributeBag attrs)
    {
        if (node.Children.Count == 0) return Ternary.False; // empty OR is vacuously false

        var sawIndeterminate = false;
        foreach (var child in node.Children)
        {
            switch (Evaluate(child, attrs))
            {
                case Ternary.True: return Ternary.True;     // one true settles an OR
                case Ternary.Indeterminate: sawIndeterminate = true; break;
            }
        }
        return sawIndeterminate ? Ternary.Indeterminate : Ternary.False;
    }

    private static Ternary EvaluateSingleChild(AbacCondition node, AbacAttributeBag attrs)
        => node.Children.Count == 1
            ? Evaluate(node.Children[0], attrs)
            : Ternary.Indeterminate; // a NOT with 0 or 2+ children is malformed

    private static Ternary Negate(Ternary v) => v switch
    {
        Ternary.True  => Ternary.False,
        Ternary.False => Ternary.True,
        _             => Ternary.Indeterminate,
    };

    private static Ternary EvaluateCompare(AbacCondition node, AbacAttributeBag attrs)
    {
        if (node.LeftAttribute is null || node.Operator is null) return Ternary.Indeterminate;

        // within_scope reads several attributes at once, so it is handled before the generic path.
        if (node.Operator == ConditionOperator.WithinScope) return EvaluateWithinScope(attrs);

        if (!attrs.TryGet(node.LeftAttribute, out var left))
            return Ternary.Indeterminate; // unresolved — fail closed

        switch (node.Operator)
        {
            case ConditionOperator.IsNull:    return Bool(left is null);
            case ConditionOperator.IsNotNull: return Bool(left is not null);
        }

        // Resolve the comparand. RightKind=attribute reads another attribute; anything else is the
        // literal carried on the row.
        object? right;
        if (string.Equals(node.RightKind, "attribute", StringComparison.OrdinalIgnoreCase))
        {
            var rightKey = AsString(node.RightValue);
            if (rightKey is null || !attrs.TryGet(rightKey, out right))
                return Ternary.Indeterminate;
        }
        else
        {
            right = node.RightValue;
        }

        return node.Operator switch
        {
            ConditionOperator.Eq         => Bool(ValuesEqual(left, right)),
            ConditionOperator.Neq        => Negate(Bool(ValuesEqual(left, right))),
            ConditionOperator.In         => Membership(left, right),
            ConditionOperator.NotIn      => Negate(Membership(left, right)),
            ConditionOperator.Contains   => Contains(left, right),
            ConditionOperator.StartsWith => StartsWith(left, right),
            ConditionOperator.Lt         => CompareOp(left, right, c => c < 0),
            ConditionOperator.Lte        => CompareOp(left, right, c => c <= 0),
            ConditionOperator.Gt         => CompareOp(left, right, c => c > 0),
            ConditionOperator.Gte        => CompareOp(left, right, c => c >= 0),
            ConditionOperator.OlderThan  => Age(left, right, attrs, olderThan: true),
            ConditionOperator.NewerThan  => Age(left, right, attrs, olderThan: false),
            _                            => Ternary.Indeterminate,
        };
    }

    /// <summary>
    /// The §6 ancestor-or-self boundary, as an operator. Mirrors HttpContextCurrentUser.IsWithinScope
    /// — a platform node matches everything; otherwise one of the subject's membership nodes must
    /// match the resource at the same level.
    ///
    /// It deliberately does NOT reproduce that method's two fail-open behaviours: there is no
    /// IsPlatformAdmin short-circuit (a platform membership must be present in scope_nodes to pass)
    /// and an absent scope_nodes claim is Indeterminate, not allow. Those are tasks A7.3 and A7.4.
    /// </summary>
    private static Ternary EvaluateWithinScope(AbacAttributeBag attrs)
    {
        if (!attrs.TryGet(AbacAttributeKeys.SubjectScopeNodes, out var rawNodes))
            return Ternary.Indeterminate;

        var nodes = ToStringList(rawNodes);
        if (nodes.Count == 0) return Ternary.False; // present but empty = no memberships = deny

        Guid? Target(string key) =>
            attrs.TryGet(key, out var v) ? AsGuid(v) : null;

        var brand     = Target(AbacAttributeKeys.ResourceBrandId);
        var franchise = Target(AbacAttributeKeys.ResourceFranchiseId);
        var store     = Target(AbacAttributeKeys.ResourceStoreId);
        var warehouse = Target(AbacAttributeKeys.ResourceWarehouseId);

        foreach (var raw in nodes)
        {
            if (!AbacScopeNode.TryParse(raw, out var parsed)) continue;
            var node = parsed.Value;

            if (string.Equals(node.ScopeType, AbacScopeNode.Platform, StringComparison.OrdinalIgnoreCase))
                return Ternary.True; // platform is an ancestor of every node

            var match = node.ScopeType.ToLowerInvariant() switch
            {
                "brand"     => Matches(node.ScopeId, brand),
                "franchise" => Matches(node.ScopeId, franchise),
                "store"     => Matches(node.ScopeId, store),
                "warehouse" => Matches(node.ScopeId, warehouse),
                _           => false,
            };
            if (match) return Ternary.True;
        }
        return Ternary.False;

        static bool Matches(Guid? node, Guid? target) => node is { } n && target is { } t && n == t;
    }

    // ── comparison helpers ────────────────────────────────────────────────────────────────────

    private static Ternary Bool(bool b) => b ? Ternary.True : Ternary.False;

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        if (TryNumeric(a, out var na) && TryNumeric(b, out var nb)) return na == nb;
        if (TryDate(a, out var da) && TryDate(b, out var db)) return da == db;
        if (a is bool ba && TryBool(b, out var bb)) return ba == bb;

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static Ternary Membership(object? left, object? right)
    {
        if (right is not IEnumerable set || right is string) return Ternary.Indeterminate;
        foreach (var item in set)
            if (ValuesEqual(left, item)) return Ternary.True;
        return Ternary.False;
    }

    /// <summary>Collection membership when the LEFT side is a list (e.g. subject.roles contains
    /// "auditor"); substring when the left side is text.</summary>
    private static Ternary Contains(object? left, object? right)
    {
        if (left is null || right is null) return Ternary.False;

        if (left is IEnumerable seq && left is not string)
        {
            foreach (var item in seq)
                if (ValuesEqual(item, right)) return Ternary.True;
            return Ternary.False;
        }

        return Bool(Normalize(left).Contains(Normalize(right), StringComparison.OrdinalIgnoreCase));
    }

    private static Ternary StartsWith(object? left, object? right)
        => left is null || right is null
            ? Ternary.False
            : Bool(Normalize(left).StartsWith(Normalize(right), StringComparison.OrdinalIgnoreCase));

    private static Ternary CompareOp(object? left, object? right, Func<int, bool> accept)
    {
        var c = CompareValues(left, right);
        return c is null ? Ternary.Indeterminate : Bool(accept(c.Value));
    }

    private static int? CompareValues(object? a, object? b)
    {
        if (a is null || b is null) return null;
        if (TryNumeric(a, out var na) && TryNumeric(b, out var nb)) return na.CompareTo(nb);
        if (TryDate(a, out var da) && TryDate(b, out var db)) return da.CompareTo(db);
        return null; // ordering text or guids is not meaningful here — fail closed
    }

    /// <summary>Time-window operators. `right` is a count of seconds; the reference clock is
    /// env.now when the resolver supplied it, so a decision is reproducible in a replay.</summary>
    private static Ternary Age(object? left, object? right, AbacAttributeBag attrs, bool olderThan)
    {
        if (!TryDate(left, out var at) || !TryNumeric(right, out var seconds)) return Ternary.Indeterminate;

        DateTimeOffset now;
        if (attrs.TryGet(AbacAttributeKeys.EnvNow, out var rawNow) && TryDate(rawNow, out var suppliedNow))
            now = suppliedNow;
        else
            return Ternary.Indeterminate; // no clock supplied — fail closed rather than guess

        var threshold = now.AddSeconds(-(double)seconds);
        return Bool(olderThan ? at < threshold : at >= threshold);
    }

    private static bool TryNumeric(object? v, out decimal d)
    {
        switch (v)
        {
            case decimal m: d = m; return true;
            case int i: d = i; return true;
            case long l: d = l; return true;
            case double db: d = (decimal)db; return true;
            case float f: d = (decimal)f; return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var ps):
                d = ps; return true;
        }
        d = 0;
        return false;
    }

    private static bool TryDate(object? v, out DateTimeOffset dt)
    {
        switch (v)
        {
            case DateTimeOffset dto: dt = dto; return true;
            case DateTime d: dt = new DateTimeOffset(d.ToUniversalTime(), TimeSpan.Zero); return true;
            case string s when DateTimeOffset.TryParse(
                    s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var ps):
                dt = ps; return true;
        }
        dt = default;
        return false;
    }

    private static bool TryBool(object v, out bool b)
    {
        switch (v)
        {
            case bool bb: b = bb; return true;
            case string s when bool.TryParse(s, out var ps): b = ps; return true;
        }
        b = false;
        return false;
    }

    private static Guid? AsGuid(object? v) => v switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var pg) => pg,
        _ => null,
    };

    private static string? AsString(object? v) => v as string ?? v?.ToString();

    /// <summary>Canonical text form so a Guid literal from JSON and a Guid attribute compare equal.</summary>
    private static string Normalize(object v) => v switch
    {
        Guid g => g.ToString("D"),
        DateTimeOffset d => d.ToUniversalTime().ToString("O"),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static List<string> ToStringList(object? v)
    {
        switch (v)
        {
            case null: return [];
            case string s:
                return s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
            case IEnumerable seq:
                var list = new List<string>();
                foreach (var item in seq)
                    if (item is not null) list.Add(item.ToString() ?? string.Empty);
                return list;
            default:
                return [];
        }
    }
}
