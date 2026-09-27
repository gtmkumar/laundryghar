using Npgsql;
using NpgsqlTypes;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>A policy as the authoring UI sees it (A8.1).</summary>
public sealed record PolicyListItem(
    Guid Id,
    string Key,
    int Version,
    Guid? BrandId,
    string? Description,
    string Effect,
    string ResourceType,
    string Action,
    string? PermissionCode,
    int Priority,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    int ConditionCount,
    bool IsPlatformAuthored);

/// <summary>One row of the explain view (A8.2).</summary>
public sealed record DecisionLogItem(
    DateTimeOffset OccurredAt,
    Guid? UserId,
    Guid? CustomerId,
    string? TokenUse,
    string? ResourceType,
    Guid? ResourceId,
    string? Action,
    string Decision,
    string Mode,
    string? MatchedPolicy,
    string? Reason,
    string? Attributes,
    bool? RbacAllowed,
    int? LatencyMicroseconds);

/// <summary>What a brand admin may change about a policy. Deliberately small.</summary>
public sealed record PolicyEdit(bool? IsActive, int? Priority, DateTimeOffset? EffectiveTo, string? Description);

/// <summary>
/// Read/write access to <c>authz.policy</c> for the admin console (A8).
///
/// <para>Raw SQL rather than EF on purpose: the <c>authz</c> schema is unmapped so that a policy
/// read can never be swept into an application <c>SaveChanges</c>, and mapping it here just to
/// serve one screen would give that up.</para>
/// </summary>
public interface IPolicyAdminService
{
    Task<IReadOnlyList<PolicyListItem>> ListAsync(
        Guid? brandId, string? resourceType, string? search, CancellationToken ct);

    Task<IReadOnlyList<DecisionLogItem>> RecentDecisionsAsync(
        Guid? brandId, Guid? userId, bool deniesOnly, int limit, CancellationToken ct);

    /// <summary>Applies an edit. Returns false when the policy does not exist or the caller's brand
    /// may not touch it.</summary>
    Task<bool> UpdateAsync(Guid policyId, Guid? actorBrandId, Guid? actorId, PolicyEdit edit, CancellationToken ct);
}

/// <inheritdoc cref="IPolicyAdminService"/>
public sealed class PolicyAdminService : IPolicyAdminService
{
    /// <summary>PostgreSQL "undefined_table" — the authz schema has not been migrated in yet.</summary>
    private const string UndefinedTable = "42P01";

    private readonly IAbacConnectionFactory _connections;
    private readonly IPolicyRepository _cache;

    public PolicyAdminService(IAbacConnectionFactory connections, IPolicyRepository cache)
    {
        _connections = connections;
        _cache = cache;
    }

    /// <summary>
    /// True when the failure is simply "the ABAC migrations have not been applied here".
    ///
    /// <para>Found by the live audit: on a database without migration 0024 this screen answered
    /// <b>500</b> to every caller. An un-migrated environment is an expected state — the engine
    /// ships disabled and the migrations are applied deliberately — so the console has to say
    /// "nothing here yet" rather than "the server broke". A 500 also tells an operator nothing
    /// about what to do next, and would page somebody.</para>
    ///
    /// <para>Narrow on purpose: only <c>undefined_table</c>. A permission error or a connection
    /// failure is a real fault and must keep surfacing as one.</para>
    /// </summary>
    private static bool IsSchemaMissing(PostgresException e) => e.SqlState == UndefinedTable;

    public async Task<IReadOnlyList<PolicyListItem>> ListAsync(
        Guid? brandId, string? resourceType, string? search, CancellationToken ct)
    {
        // brand_id IS NULL keeps platform-authored policies visible to every tenant. Omitting that
        // arm is what makes identity_access.roles show a brand admin zero of the 17 system roles,
        // and a policy console that hid every rule actually governing the tenant would be worse
        // than useless — it would read as "no rules apply to me".
        const string sql = """
            SELECT p.id, p.key, p.version, p.brand_id, p.description, p.effect,
                   p.resource_type, p.action, p.permission_code, p.priority,
                   p.effective_from, p.effective_to, p.is_active,
                   (SELECT count(*) FROM authz.policy_condition c WHERE c.policy_id = p.id)
            FROM authz.policy p
            WHERE (@brand_id::uuid IS NULL OR p.brand_id IS NULL OR p.brand_id = @brand_id)
              AND (@resource_type::text IS NULL OR p.resource_type = @resource_type)
              AND (@search::text IS NULL
                   OR p.key ILIKE '%' || @search || '%'
                   OR coalesce(p.permission_code, '') ILIKE '%' || @search || '%'
                   OR coalesce(p.description, '')     ILIKE '%' || @search || '%')
            ORDER BY p.resource_type, p.action, p.priority, p.key
            LIMIT 500
            """;

        await using var conn = await _connections.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("brand_id", NpgsqlDbType.Uuid)
            { Value = (object?)brandId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("resource_type", NpgsqlDbType.Text)
            { Value = (object?)resourceType ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text)
            { Value = (object?)search ?? DBNull.Value });

        var results = new List<PolicyListItem>();

        NpgsqlDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct);
        }
        catch (PostgresException e) when (IsSchemaMissing(e))
        {
            return results;   // not migrated yet → an empty console, not a 500
        }

        await using var _ = reader;
        while (await reader.ReadAsync(ct))
        {
            var policyBrand = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
            results.Add(new PolicyListItem(
                Id: reader.GetGuid(0),
                Key: reader.GetString(1),
                Version: reader.GetInt32(2),
                BrandId: policyBrand,
                Description: reader.IsDBNull(4) ? null : reader.GetString(4),
                Effect: reader.GetString(5),
                ResourceType: reader.GetString(6),
                Action: reader.GetString(7),
                PermissionCode: reader.IsDBNull(8) ? null : reader.GetString(8),
                Priority: reader.GetInt32(9),
                EffectiveFrom: reader.GetFieldValue<DateTimeOffset>(10),
                EffectiveTo: reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                IsActive: reader.GetBoolean(12),
                ConditionCount: (int)reader.GetInt64(13),
                IsPlatformAuthored: policyBrand is null));
        }

        return results;
    }

    public async Task<IReadOnlyList<DecisionLogItem>> RecentDecisionsAsync(
        Guid? brandId, Guid? userId, bool deniesOnly, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT occurred_at, user_id, customer_id, token_use, resource_type, resource_id,
                   action, decision, mode, matched_policy, reason, attributes::text,
                   rbac_allowed, latency_us
            FROM authz.decision_log
            WHERE (@brand_id::uuid IS NULL OR brand_id = @brand_id)
              AND (@user_id::uuid  IS NULL OR user_id  = @user_id)
              AND (NOT @denies_only OR decision <> 'permit')
            ORDER BY occurred_at DESC
            LIMIT @limit
            """;

        await using var conn = await _connections.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("brand_id", NpgsqlDbType.Uuid)
            { Value = (object?)brandId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Uuid)
            { Value = (object?)userId ?? DBNull.Value });
        cmd.Parameters.AddWithValue("denies_only", deniesOnly);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));

        var results = new List<DecisionLogItem>();

        NpgsqlDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct);
        }
        catch (PostgresException e) when (IsSchemaMissing(e))
        {
            return results;
        }

        await using var _ = reader;
        while (await reader.ReadAsync(ct))
            results.Add(new DecisionLogItem(
                OccurredAt: reader.GetFieldValue<DateTimeOffset>(0),
                UserId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                CustomerId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                TokenUse: reader.IsDBNull(3) ? null : reader.GetString(3),
                ResourceType: reader.IsDBNull(4) ? null : reader.GetString(4),
                ResourceId: reader.IsDBNull(5) ? null : reader.GetGuid(5),
                Action: reader.IsDBNull(6) ? null : reader.GetString(6),
                Decision: reader.GetString(7),
                Mode: reader.GetString(8),
                MatchedPolicy: reader.IsDBNull(9) ? null : reader.GetString(9),
                Reason: reader.IsDBNull(10) ? null : reader.GetString(10),
                Attributes: reader.IsDBNull(11) ? null : reader.GetString(11),
                RbacAllowed: reader.IsDBNull(12) ? null : reader.GetBoolean(12),
                LatencyMicroseconds: reader.IsDBNull(13) ? null : reader.GetInt32(13)));

        return results;
    }

    public async Task<bool> UpdateAsync(
        Guid policyId, Guid? actorBrandId, Guid? actorId, PolicyEdit edit, CancellationToken ct)
    {
        // A8.3 — a brand may TIGHTEN a platform policy, never LOOSEN it. The two ways to loosen one
        // are deactivating it and expiring it, so both are refused on a platform-authored row; a
        // brand that wants a platform rule not to apply has to ask the platform. Everything a brand
        // CAN do to its own policies is allowed here, and priority is left editable on platform
        // rows because reordering cannot make a deny stop denying — deny-overrides runs first
        // regardless of priority.
        const string sql = """
            UPDATE authz.policy p
               SET is_active      = coalesce(@is_active, p.is_active),
                   priority       = coalesce(@priority, p.priority),
                   effective_to   = CASE WHEN @set_effective_to THEN @effective_to ELSE p.effective_to END,
                   description    = coalesce(@description, p.description),
                   updated_at     = now(),
                   updated_by     = @actor_id
             WHERE p.id = @id
               -- A brand may only touch its own rows, or a platform row in a tightening way.
               AND (p.brand_id = @actor_brand_id
                    OR @actor_brand_id IS NULL
                    OR (p.brand_id IS NULL
                        AND coalesce(@is_active, true) = true
                        AND NOT @set_effective_to))
            """;

        await using var conn = await _connections.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", policyId);
        cmd.Parameters.Add(new NpgsqlParameter("actor_brand_id", NpgsqlDbType.Uuid)
            { Value = (object?)actorBrandId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("actor_id", NpgsqlDbType.Uuid)
            { Value = (object?)actorId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlDbType.Boolean)
            { Value = (object?)edit.IsActive ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("priority", NpgsqlDbType.Integer)
            { Value = (object?)edit.Priority ?? DBNull.Value });
        cmd.Parameters.AddWithValue("set_effective_to", edit.EffectiveTo is not null);
        cmd.Parameters.Add(new NpgsqlParameter("effective_to", NpgsqlDbType.TimestampTz)
            { Value = (object?)edit.EffectiveTo ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("description", NpgsqlDbType.Text)
            { Value = (object?)edit.Description ?? DBNull.Value });

        int rows;
        try
        {
            rows = await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException e) when (IsSchemaMissing(e))
        {
            return false;     // surfaces as the 404 "not editable" response
        }

        // Drop the cache so the author sees their own edit on the next request rather than up to
        // one 15-second window later — the single most confusing thing about a cached policy store.
        if (rows > 0) _cache.Invalidate();

        return rows > 0;
    }
}
