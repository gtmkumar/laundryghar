using System.Linq.Expressions;
using System.Reflection;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Compiles the same <c>authz.policy</c> rows the PDP evaluates into an EF predicate (A4.3), so a
/// list endpoint filters in the database instead of loading rows and denying them one at a time.
///
/// <para><b>Why this must be the same rows and not a parallel rule set.</b> The moment list
/// filtering and per-row decisions come from different sources they drift, and the drift is
/// invisible: the list quietly shows a row the detail endpoint then refuses to open, or worse, hides
/// one it would have allowed. Sharing the row store means a policy edit moves both at once.</para>
/// </summary>
public interface IPolicyFilterBuilder
{
    /// <summary>
    /// The predicate for "which rows of <typeparamref name="T"/> may this subject see".
    /// <paramref name="attributeToProperty"/> maps an ABAC attribute key to a property on
    /// <typeparamref name="T"/> — the in-memory twin of <c>authz.resource_type.attribute_map</c>.
    /// </summary>
    Task<Expression<Func<T, bool>>> BuildAsync<T>(
        string resourceType,
        string action,
        IReadOnlyDictionary<string, string> attributeToProperty,
        CancellationToken ct);
}

/// <inheritdoc cref="IPolicyFilterBuilder"/>
public sealed class PolicyFilterBuilder : IPolicyFilterBuilder
{
    private readonly IPolicyRepository _policies;
    private readonly IAbacAttributeProvider _attributes;
    private readonly AbacOptions _options;

    public PolicyFilterBuilder(
        IPolicyRepository policies,
        IAbacAttributeProvider attributes,
        Microsoft.Extensions.Options.IOptions<AbacOptions> options)
    {
        _policies = policies;
        _attributes = attributes;
        _options = options.Value;
    }

    public async Task<Expression<Func<T, bool>>> BuildAsync<T>(
        string resourceType,
        string action,
        IReadOnlyDictionary<string, string> attributeToProperty,
        CancellationToken ct)
    {
        // Disabled or not yet enforced for this type: the caller's existing filtering is unchanged.
        if (!_options.EnforcesFor(resourceType)) return _ => true;

        var bag = await _attributes.BuildAsync(resourceType, action, null, ct);
        var policies = await _policies.GetAsync(resourceType, action, ct);

        var parameter = Expression.Parameter(typeof(T), "row");
        var translator = new Translator(typeof(T), parameter, attributeToProperty, bag);

        var now = bag.TryGet(AbacAttributeKeys.EnvNow, out var n) && n is DateTimeOffset dto
            ? dto
            : DateTimeOffset.UtcNow;

        Expression? permits = null;
        Expression? denies = null;

        foreach (var policy in policies.Where(p => p.IsInEffect(now)).OrderBy(p => p.Priority))
        {
            var brandOk = policy.BrandId is null
                       || (bag.TryGet(AbacAttributeKeys.SubjectBrandId, out var sb)
                           && sb is Guid sbg && sbg == policy.BrandId);
            if (!brandOk) continue;

            var translated = translator.TryTranslate(policy.Condition);

            if (policy.Effect == PolicyEffect.Deny)
            {
                // A deny we cannot express in SQL must hide everything, exactly as an Indeterminate
                // deny denies in the PDP. Anything else would let a deny vanish at the list layer
                // while still firing on the detail layer.
                var body = translated ?? Expression.Constant(true);
                denies = denies is null ? body : Expression.OrElse(denies, body);
            }
            else
            {
                // An untranslatable permit contributes nothing — it cannot widen the result set.
                if (translated is null) continue;
                permits = permits is null ? translated : Expression.OrElse(permits, translated);
            }
        }

        // Default-deny: no permit matched means no rows, mirroring the PDP's final fallthrough.
        Expression result = permits ?? Expression.Constant(false);
        if (denies is not null)
            result = Expression.AndAlso(result, Expression.Not(denies));

        return Expression.Lambda<Func<T, bool>>(result, parameter);
    }

    /// <summary>
    /// Turns one condition tree into an expression, or null when any part of it cannot be expressed
    /// over the row — a null propagates all the way up, so a single untranslatable leaf makes the
    /// whole policy untranslatable rather than silently dropping that leaf. Dropping a leaf from an
    /// AND is how a filter accidentally widens.
    /// </summary>
    private sealed class Translator
    {
        private readonly Type _rowType;
        private readonly ParameterExpression _row;
        private readonly IReadOnlyDictionary<string, string> _map;
        private readonly AbacAttributeBag _bag;

        public Translator(
            Type rowType, ParameterExpression row,
            IReadOnlyDictionary<string, string> map, AbacAttributeBag bag)
        {
            _rowType = rowType;
            _row = row;
            _map = map;
            _bag = bag;
        }

        public Expression? TryTranslate(AbacCondition? node)
        {
            if (node is null) return Expression.Constant(true); // unconditional policy

            switch (node.NodeType)
            {
                case ConditionNodeType.And:
                case ConditionNodeType.Or:
                {
                    // Empty AND is vacuously true, empty OR vacuously false — the same asymmetry
                    // ConditionEvaluator.EvaluateAnd/EvaluateOr encode. Collapsing both to true
                    // would make an empty OR inside a permit match every row.
                    if (node.Children.Count == 0)
                        return Expression.Constant(node.NodeType == ConditionNodeType.And);
                    Expression? acc = null;
                    foreach (var child in node.Children)
                    {
                        var t = TryTranslate(child);
                        if (t is null) return null;
                        acc = acc is null
                            ? t
                            : node.NodeType == ConditionNodeType.And
                                ? Expression.AndAlso(acc, t)
                                : Expression.OrElse(acc, t);
                    }
                    return acc;
                }

                case ConditionNodeType.Not:
                {
                    if (node.Children.Count != 1) return null;
                    var inner = TryTranslate(node.Children[0]);
                    return inner is null ? null : Expression.Not(inner);
                }

                case ConditionNodeType.Compare:
                    return TryCompare(node);

                default:
                    return null;
            }
        }

        private Expression? TryCompare(AbacCondition node)
        {
            if (node.Operator == ConditionOperator.WithinScope) return TryWithinScope();
            if (node.LeftAttribute is null || node.Operator is null) return null;

            var left = Operand(node.LeftAttribute);
            if (left is null) return null;

            switch (node.Operator)
            {
                case ConditionOperator.IsNull:
                    return IsNullExpression(left, negate: false);
                case ConditionOperator.IsNotNull:
                    return IsNullExpression(left, negate: true);
            }

            object? rightValue;
            if (string.Equals(node.RightKind, "attribute", StringComparison.OrdinalIgnoreCase))
            {
                var key = node.RightValue?.ToString();
                if (key is null) return null;

                // A right-hand ROW attribute (column vs column) is legitimate; anything else must be
                // resolvable from the bag now, or the comparison is not expressible.
                if (_map.ContainsKey(key))
                {
                    var rightExpr = Operand(key);
                    return rightExpr is null ? null : Compare(node.Operator, left, rightExpr);
                }
                if (!_bag.TryGet(key, out rightValue)) return null;
            }
            else
            {
                rightValue = node.RightValue;
            }

            return node.Operator is ConditionOperator.In or ConditionOperator.NotIn
                ? InExpression(left, rightValue, node.Operator == ConditionOperator.NotIn)
                : node.Operator is ConditionOperator.OlderThan or ConditionOperator.NewerThan
                    ? AgeExpression(left, rightValue, node.Operator == ConditionOperator.OlderThan)
                    : Compare(node.Operator, left, Constant(rightValue, left.Type));
        }

        /// <summary>
        /// The scope boundary as a row predicate: the row's brand/franchise/store/warehouse must
        /// match one of the subject's membership nodes. A platform node matches every row.
        /// Absent scope_nodes is untranslatable (null), which — for a permit — contributes nothing
        /// and yields no rows. That is the same fail-closed direction the PDP takes.
        /// </summary>
        private Expression? TryWithinScope()
        {
            if (!_bag.TryGet(AbacAttributeKeys.SubjectScopeNodes, out var raw)) return null;

            var nodes = (raw as IEnumerable<string>)?.ToList()
                     ?? (raw as IEnumerable<object?>)?.Select(o => o?.ToString() ?? "").ToList()
                     ?? [];
            if (nodes.Count == 0) return Expression.Constant(false);

            var byLevel = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawNode in nodes)
            {
                if (!AbacScopeNode.TryParse(rawNode, out var parsed)) continue;
                if (parsed.Value.ScopeType.Equals(AbacScopeNode.Platform, StringComparison.OrdinalIgnoreCase))
                    return Expression.Constant(true);
                if (parsed.Value.ScopeId is not { } id) continue;

                if (!byLevel.TryGetValue(parsed.Value.ScopeType, out var list))
                    byLevel[parsed.Value.ScopeType] = list = [];
                list.Add(id);
            }

            Expression? acc = null;
            foreach (var (level, ids) in byLevel)
            {
                var attribute = level.ToLowerInvariant() switch
                {
                    "brand"     => AbacAttributeKeys.ResourceBrandId,
                    "franchise" => AbacAttributeKeys.ResourceFranchiseId,
                    "store"     => AbacAttributeKeys.ResourceStoreId,
                    "warehouse" => AbacAttributeKeys.ResourceWarehouseId,
                    _           => null,
                };
                if (attribute is null) continue;

                var column = Operand(attribute);
                if (column is null) continue; // this row type has no such column — skip the level

                var clause = InExpression(column, ids, negate: false);
                if (clause is null) continue;
                acc = acc is null ? clause : Expression.OrElse(acc, clause);
            }

            return acc ?? Expression.Constant(false);
        }

        /// <summary>A row column when the attribute is mapped, otherwise a constant from the bag.</summary>
        private Expression? Operand(string attributeKey)
        {
            if (_map.TryGetValue(attributeKey, out var propertyName))
            {
                var property = _rowType.GetProperty(
                    propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return property is null ? null : Expression.Property(_row, property);
            }

            return _bag.TryGet(attributeKey, out var value)
                ? Expression.Constant(value, value?.GetType() ?? typeof(object))
                : null;
        }

        private static Expression? Compare(string op, Expression left, Expression? right)
        {
            if (right is null) return null;
            if (!Align(ref left, ref right)) return null;

            return op switch
            {
                ConditionOperator.Eq  => Expression.Equal(left, right),
                ConditionOperator.Neq => Expression.NotEqual(left, right),
                ConditionOperator.Lt  => Expression.LessThan(left, right),
                ConditionOperator.Lte => Expression.LessThanOrEqual(left, right),
                ConditionOperator.Gt  => Expression.GreaterThan(left, right),
                ConditionOperator.Gte => Expression.GreaterThanOrEqual(left, right),
                ConditionOperator.Contains   => StringCall(left, right, nameof(string.Contains)),
                ConditionOperator.StartsWith => StringCall(left, right, nameof(string.StartsWith)),
                _ => null,
            };
        }

        private static Expression? StringCall(Expression left, Expression right, string method)
        {
            if (left.Type != typeof(string) || right.Type != typeof(string)) return null;
            var mi = typeof(string).GetMethod(method, [typeof(string)]);
            return mi is null
                ? null
                // A null column would throw inside the call; guard so it is simply false.
                : Expression.AndAlso(
                    Expression.NotEqual(left, Expression.Constant(null, typeof(string))),
                    Expression.Call(left, mi, right));
        }

        private static Expression? InExpression(Expression left, object? rightValue, bool negate)
        {
            var items = rightValue switch
            {
                null => null,
                string s => [s],
                System.Collections.IEnumerable e and not string
                    => e.Cast<object?>().ToList(),
                _ => new List<object?> { rightValue },
            };
            if (items is null) return null;
            if (items.Count == 0) return Expression.Constant(negate);

            var underlying = Nullable.GetUnderlyingType(left.Type) ?? left.Type;

            // An OR-chain of equalities rather than List.Contains: it translates on every provider,
            // and it lets each item be coerced to the column's type independently.
            Expression? acc = null;
            foreach (var item in items)
            {
                var converted = Coerce(item, underlying);
                if (converted is null) return null;

                Expression right = Expression.Constant(converted, underlying);
                Expression target = left;
                if (!Align(ref target, ref right)) return null;

                var eq = Expression.Equal(target, right);
                acc = acc is null ? eq : Expression.OrElse(acc, eq);
            }

            return negate ? Expression.Not(acc!) : acc!;
        }

        /// <summary>older_than / newer_than against a fixed <c>env.now</c>, so the boundary is a
        /// constant the database can use an index against rather than a function of each row.</summary>
        private Expression? AgeExpression(Expression left, object? seconds, bool olderThan)
        {
            if (!_bag.TryGet(AbacAttributeKeys.EnvNow, out var nowRaw) || nowRaw is not DateTimeOffset now)
                return null;
            if (!TryDouble(seconds, out var s)) return null;

            var boundary = now.AddSeconds(-s);
            var underlying = Nullable.GetUnderlyingType(left.Type) ?? left.Type;

            object boundaryValue;
            if (underlying == typeof(DateTimeOffset)) boundaryValue = boundary;
            else if (underlying == typeof(DateTime)) boundaryValue = boundary.UtcDateTime;
            else return null;

            Expression right = Expression.Constant(boundaryValue, underlying);
            var target = left;
            if (!Align(ref target, ref right)) return null;

            return olderThan
                ? Expression.LessThan(target, right)
                : Expression.GreaterThanOrEqual(target, right);
        }

        private static Expression IsNullExpression(Expression operand, bool negate)
        {
            if (operand.Type.IsValueType && Nullable.GetUnderlyingType(operand.Type) is null)
                return Expression.Constant(negate); // a non-nullable column is never null

            var test = Expression.Equal(operand, Expression.Constant(null, operand.Type));
            return negate ? Expression.Not(test) : test;
        }

        private static Expression? Constant(object? value, Type targetType)
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is null)
                return underlying.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                    ? null
                    : Expression.Constant(null, targetType);

            var converted = Coerce(value, underlying);
            return converted is null ? null : Expression.Constant(converted, underlying);
        }

        /// <summary>Lift both sides to a common type so Expression.Equal does not throw on
        /// Guid? vs Guid — the single most common shape here, since almost every scope column is
        /// nullable and almost every subject value is not.</summary>
        private static bool Align(ref Expression left, ref Expression right)
        {
            if (left.Type == right.Type) return true;

            var leftUnderlying = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
            var rightUnderlying = Nullable.GetUnderlyingType(right.Type) ?? right.Type;
            if (leftUnderlying != rightUnderlying) return false;

            var lifted = typeof(Nullable<>).MakeGenericType(leftUnderlying);
            if (left.Type != lifted) left = Expression.Convert(left, lifted);
            if (right.Type != lifted) right = Expression.Convert(right, lifted);
            return true;
        }

        private static object? Coerce(object? value, Type target)
        {
            if (value is null) return null;
            if (target.IsInstanceOfType(value)) return value;

            try
            {
                if (target == typeof(Guid))
                    return value is Guid g ? g
                         : Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
                if (target == typeof(string)) return value.ToString();
                if (target == typeof(DateTimeOffset))
                    return value is DateTimeOffset d ? d
                         : DateTimeOffset.TryParse(value.ToString(), out var dt) ? dt : null;
                if (target == typeof(DateTime))
                    return value is DateTime dtv ? dtv
                         : DateTime.TryParse(value.ToString(), out var dtp) ? dtp : null;
                if (target.IsEnum) return Enum.Parse(target, value.ToString()!, ignoreCase: true);
                return Convert.ChangeType(value, target);
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                return null;
            }
        }

        private static bool TryDouble(object? value, out double result)
        {
            switch (value)
            {
                case null: result = 0; return false;
                case double d: result = d; return true;
                case int i: result = i; return true;
                case long l: result = l; return true;
                case decimal m: result = (double)m; return true;
                default: return double.TryParse(value.ToString(), out result);
            }
        }
    }
}
