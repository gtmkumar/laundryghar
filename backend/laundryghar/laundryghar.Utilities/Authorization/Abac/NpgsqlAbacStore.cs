using System.Data;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>Where the ABAC engine reads its policies from. Separated so tests can hand the PDP a
/// literal list without a database.</summary>
public interface IPolicySource
{
    /// <summary>Every active policy with its condition tree. The whole set is small (hundreds of
    /// rows) so it is loaded wholesale and filtered in memory — one query per cache window rather
    /// than one per decision.</summary>
    Task<IReadOnlyList<AbacPolicy>> LoadAllAsync(CancellationToken ct);

    /// <summary>Cheap change-detector: the greatest updated_at plus the row count. A version that
    /// has not moved means the cached set is still current.</summary>
    Task<string> GetVersionAsync(CancellationToken ct);
}

/// <summary>Opens connections for the ABAC reads. One implementation per host, all pointing at the
/// same canonical database.</summary>
public interface IAbacConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct);
}

/// <inheritdoc cref="IAbacConnectionFactory"/>
public sealed class NpgsqlAbacConnectionFactory : IAbacConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlAbacConnectionFactory(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        => await _dataSource.OpenConnectionAsync(ct);
}

/// <summary>
/// Reads <c>authz.policy</c> + <c>authz.policy_condition</c> and rebuilds the condition trees.
///
/// <para><b>Why this bypasses RLS deliberately.</b> The connection used here does not set
/// <c>app.current_brand_id</c>, so it reads every brand's policies. That is correct and necessary:
/// the PDP filters by brand itself in <c>PolicyDecisionPoint.IsApplicable</c>, and a brand-filtered
/// read would hide the platform-authored deny policies that must bind every tenant. The connection
/// is used for nothing else, and the role it runs as has SELECT only on these two tables.</para>
/// </summary>
public sealed class NpgsqlPolicySource : IPolicySource
{
    private readonly IAbacConnectionFactory _connections;

    public NpgsqlPolicySource(IAbacConnectionFactory connections) => _connections = connections;

    private const string PolicySql = """
        SELECT id, key, effect, resource_type, action, brand_id, permission_code,
               priority, effective_from, effective_to, is_active
        FROM authz.policy
        WHERE is_active
        """;

    private const string ConditionSql = """
        SELECT id, policy_id, parent_id, node_type, left_attribute, operator,
               right_kind, right_value, sort_order
        FROM authz.policy_condition
        ORDER BY policy_id, sort_order, id
        """;

    public async Task<IReadOnlyList<AbacPolicy>> LoadAllAsync(CancellationToken ct)
    {
        await using var conn = await _connections.OpenAsync(ct);

        var headers = new List<PolicyHeader>();
        await using (var cmd = new NpgsqlCommand(PolicySql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                headers.Add(new PolicyHeader(
                    Id: reader.GetGuid(0),
                    Key: reader.GetString(1),
                    Effect: reader.GetString(2),
                    ResourceType: reader.GetString(3),
                    Action: reader.GetString(4),
                    BrandId: reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    PermissionCode: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Priority: reader.GetInt32(7),
                    EffectiveFrom: reader.GetFieldValue<DateTimeOffset>(8),
                    EffectiveTo: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                    IsActive: reader.GetBoolean(10)));
        }

        var rows = new List<ConditionRow>();
        await using (var cmd = new NpgsqlCommand(ConditionSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(new ConditionRow(
                    Id: reader.GetGuid(0),
                    PolicyId: reader.GetGuid(1),
                    ParentId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    NodeType: reader.GetString(3),
                    LeftAttribute: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Operator: reader.IsDBNull(5) ? null : reader.GetString(5),
                    RightKind: reader.IsDBNull(6) ? null : reader.GetString(6),
                    RightValueJson: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        var byPolicy = rows.GroupBy(r => r.PolicyId)
                           .ToDictionary(g => g.Key, g => g.ToList());

        return headers
            .Select(h => new AbacPolicy
            {
                Key = h.Key,
                Effect = h.Effect,
                ResourceType = h.ResourceType,
                Action = h.Action,
                BrandId = h.BrandId,
                PermissionCode = h.PermissionCode,
                Priority = h.Priority,
                EffectiveFrom = h.EffectiveFrom,
                EffectiveTo = h.EffectiveTo,
                IsActive = h.IsActive,
                Condition = byPolicy.TryGetValue(h.Id, out var conditions)
                    ? BuildTree(conditions)
                    : null,
            })
            .ToList();
    }

    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        await using var conn = await _connections.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT coalesce(max(updated_at)::text, '-') || ':' || count(*)::text
            FROM authz.policy
            """, conn);
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "-";
    }

    /// <summary>
    /// Rebuilds the AND/OR/NOT tree from its flat rows. A policy whose rows contain no root (every
    /// row has a parent that is missing) yields null — unconditional — rather than a partial tree,
    /// because half a condition is more dangerous than none: a truncated AND branch silently widens
    /// a permit.
    /// </summary>
    private static AbacCondition? BuildTree(List<ConditionRow> rows)
    {
        var roots = rows.Where(r => r.ParentId is null).ToList();
        if (roots.Count == 0) return null;

        // More than one root means the author wrote sibling top-level conditions; ANDing them is
        // the tightening reading, which is the safe one for a permit and harmless for a deny.
        var built = roots.Select(r => Build(r, rows)).ToList();
        return built.Count == 1
            ? built[0]
            : new AbacCondition { NodeType = ConditionNodeType.And, Children = built };
    }

    private static AbacCondition Build(ConditionRow row, List<ConditionRow> all)
    {
        var children = all.Where(r => r.ParentId == row.Id)
                          .Select(r => Build(r, all))
                          .ToList();

        return new AbacCondition
        {
            NodeType = row.NodeType,
            LeftAttribute = row.LeftAttribute,
            Operator = row.Operator,
            RightKind = row.RightKind,
            RightValue = ParseJson(row.RightValueJson),
            Children = children,
        };
    }

    /// <summary>
    /// right_value is jsonb. It is unwrapped to a CLR value the evaluator's comparers understand —
    /// a JsonElement would compare by reference and silently make every `eq` false.
    /// </summary>
    internal static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return Unwrap(JsonDocument.Parse(json).RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? Unwrap(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(Unwrap).ToList(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => Unwrap(p.Value)),
        _ => null,
    };

    private sealed record PolicyHeader(
        Guid Id, string Key, string Effect, string ResourceType, string Action,
        Guid? BrandId, string? PermissionCode, int Priority,
        DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, bool IsActive);

    private sealed record ConditionRow(
        Guid Id, Guid PolicyId, Guid? ParentId, string NodeType,
        string? LeftAttribute, string? Operator, string? RightKind, string? RightValueJson);
}

/// <summary>
/// Supplies <c>subject.roles</c> and <c>subject.entitlements</c> from the database (A2.2), cached
/// per user for the same 15-second window the policy cache uses, so adding this resolver costs at
/// most one small query per user per window rather than one per request.
/// </summary>
public sealed class NpgsqlAbacSubjectDataSource : IAbacSubjectDataSource
{
    private readonly IAbacConnectionFactory _connections;
    private readonly IMemoryCache _cache;

    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    public NpgsqlAbacSubjectDataSource(
        IAbacConnectionFactory connections,
        IMemoryCache cache)
    {
        _connections = connections;
        _cache = cache;
    }

    private const string Sql = """
        SELECT
          coalesce((
            SELECT array_agg(DISTINCT r.code)
            FROM identity_access.user_scope_memberships m
            JOIN identity_access.roles r ON r.id = m.role_id
            WHERE m.user_id = @user_id
              AND m.revoked_at IS NULL
              AND (m.expires_at IS NULL OR m.expires_at > now())
          ), '{}') AS role_codes,
          coalesce((
            SELECT array_agg(DISTINCT f.feature_key)
            FROM identity_access.brand_feature f
            WHERE f.brand_id = @brand_id
              AND f.enabled
              AND (f.valid_until IS NULL OR f.valid_until >= current_date)
          ), '{}') AS entitlements
        """;

    public async ValueTask<AbacSubjectFacts?> GetAsync(Guid userId, Guid? brandId, CancellationToken ct)
    {
        var key = $"abac:subject:{userId}:{brandId}";
        if (_cache.TryGetValue(key, out AbacSubjectFacts? cached)) return cached;

        AbacSubjectFacts? facts;
        try
        {
            await using var conn = await _connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(Sql, conn);
            cmd.Parameters.AddWithValue("user_id", userId);
            cmd.Parameters.AddWithValue("brand_id",
                brandId.HasValue ? brandId.Value : DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            facts = await reader.ReadAsync(ct)
                ? new AbacSubjectFacts(reader.GetFieldValue<string[]>(0), reader.GetFieldValue<string[]>(1))
                : null;
        }
        catch (NpgsqlException)
        {
            // A failed read must not be cached as "no roles" — that would be a fail-open answer with
            // a 15-second lifetime. Returning null omits both keys, so any policy over them denies.
            return null;
        }

        _cache.Set(key, facts, Ttl);
        return facts;
    }
}

/// <summary>
/// Reads a resource row's attributes through <c>authz.resource_type.attribute_map</c> (A2.3) when
/// the caller supplied only an id.
///
/// <para><b>Identifier safety.</b> The schema, table and column names come from a database table,
/// not from the request — but they are still interpolated into SQL, so every one is validated
/// against <see cref="Identifier"/> and quoted. An entry that fails validation is skipped rather
/// than escaped-and-run: a resource type nobody can read fails closed, whereas a cleverly quoted
/// one might not.</para>
/// </summary>
public sealed class NpgsqlResourceAttributeResolver : IResourceAttributeResolver
{
    private static readonly Regex Identifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);

    private readonly IAbacConnectionFactory _connections;
    private readonly IMemoryCache _cache;

    public NpgsqlResourceAttributeResolver(
        IAbacConnectionFactory connections,
        IMemoryCache cache)
    {
        _connections = connections;
        _cache = cache;
    }

    public async ValueTask ResolveAsync(
        AbacAttributeBag bag, string resourceType, Guid resourceId, CancellationToken ct)
    {
        var map = await GetMapAsync(resourceType, ct);
        if (map is null || map.Columns.Count == 0) return;

        var columns = map.Columns
            .Where(kv => Identifier.IsMatch(kv.Value))
            .ToList();
        if (columns.Count == 0) return;

        var selectList = string.Join(", ", columns.Select(kv => $"\"{kv.Value}\""));
        var sql = $"SELECT {selectList} FROM \"{map.Schema}\".\"{map.Table}\" WHERE id = @id";

        try
        {
            await using var conn = await _connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", resourceId);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
            if (!await reader.ReadAsync(ct)) return; // no such row → attributes stay absent → deny

            for (var i = 0; i < columns.Count; i++)
                bag.Set(columns[i].Key, reader.IsDBNull(i) ? null : reader.GetValue(i));
        }
        catch (NpgsqlException)
        {
            // Leave the bag untouched: absent attributes are Indeterminate, which denies.
        }
    }

    private sealed record ResourceMap(string Schema, string Table, IReadOnlyDictionary<string, string> Columns);

    private async Task<ResourceMap?> GetMapAsync(string resourceType, CancellationToken ct)
    {
        var key = $"abac:restype:{resourceType}";
        if (_cache.TryGetValue(key, out ResourceMap? cached)) return cached;

        ResourceMap? map = null;
        try
        {
            await using var conn = await _connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT schema_name, table_name, attribute_map::text
                FROM authz.resource_type
                WHERE key = @key AND is_active
                """, conn);
            cmd.Parameters.AddWithValue("key", resourceType);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
            if (await reader.ReadAsync(ct))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                if (Identifier.IsMatch(schema) && Identifier.IsMatch(table))
                {
                    var columns = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2))
                                  ?? [];
                    map = new ResourceMap(schema, table, columns);
                }
            }
        }
        catch (Exception e) when (e is NpgsqlException or JsonException)
        {
            return null;
        }

        _cache.Set(key, map, TimeSpan.FromMinutes(5));
        return map;
    }
}
