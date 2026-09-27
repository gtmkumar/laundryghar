using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;

namespace laundryghar.SharedDataModel.Persistence.Interceptors;

/// <summary>
/// Sets PostgreSQL session-level config variables for Row-Level Security on every connection open.
/// The DB RLS policies read: app.current_brand_id, app.current_customer_id,
/// app.current_partner_id, app.current_user_id, and app.bypass_rls.
/// app.current_franchise_id and app.current_store_id are published here but are currently read by
/// ZERO policies — the franchise/store boundary lives only in ICurrentUser.IsWithinScope (95 call
/// sites). Closing that gap is task A4.4 / A5.3 of docs/ABAC_IMPLEMENTATION_PLAN.md.
/// <para>The five app.current_user_type / _token_use / _scope_nodes / _permissions / _roles
/// variables are the A5.2 subject slice, read by <c>authz.permits()</c> rather than by any
/// hand-written policy. They ride along in the statement that already ran, and they are what lets a
/// per-row predicate test set membership instead of walking identity_access on every row.</para>
/// Empty string is used for null/unset values — RLS policies treat empty as "unset".
/// </summary>
public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentTenant _currentTenant;

    public RlsConnectionInterceptor(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetRlsVariables(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariablesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private void SetRlsVariables(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        BuildSetConfigCommand(cmd);
        cmd.ExecuteNonQuery();
    }

    private async Task SetRlsVariablesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        BuildSetConfigCommand(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private void BuildSetConfigCommand(DbCommand cmd)
    {
        var brandId = _currentTenant.BrandId?.ToString() ?? string.Empty;
        var franchiseId = _currentTenant.FranchiseId?.ToString() ?? string.Empty;
        var storeId = _currentTenant.StoreId?.ToString() ?? string.Empty;
        var userId = _currentTenant.UserId?.ToString() ?? string.Empty;
        var partnerId = _currentTenant.PartnerId?.ToString() ?? string.Empty;
        var customerId = _currentTenant.CustomerId?.ToString() ?? string.Empty;
        var bypassRls = _currentTenant.BypassRls ? "true" : "false";

        // A5.2 — the subject slice authz.permits() reads. Every one is set on EVERY open, even when
        // null, and that is not incidental: connections are pooled, so a GUC left unwritten keeps
        // whatever the previous request on that physical connection put there. Omitting a set_config
        // for a null would leak one caller's memberships into the next caller's policy evaluation,
        // which is a cross-tenant authorization bug of the worst kind — silent, intermittent, and
        // dependent on pool scheduling.
        //
        // Three states have to survive the trip, so a null is written as the sentinel "?" rather
        // than as an empty string:
        //   "?"      unresolved — kernel.split_setting returns NULL, comparisons go indeterminate,
        //            and the combining algorithm denies.
        //   ""       resolved and empty — "this principal holds no memberships/roles", which is a
        //            real answer that correctly matches nothing.
        //   "a b c"  resolved.
        // Collapsing the first two would be a fail-open for scope_nodes (an unknown membership set
        // reading as "none" is fine) but a fail-OPEN for roles: the A6.3 deny policies are written
        // as `subject.roles contains <role>`, and an empty array makes every one of them evaluate
        // FALSE — i.e. not denied — when the truth is that we simply did not know.
        const string Unresolved = "?";
        var userType    = _currentTenant.UserType    ?? string.Empty;
        var tokenUse    = _currentTenant.TokenUse    ?? string.Empty;
        var scopeNodes  = _currentTenant.ScopeNodes  ?? Unresolved;
        var permissions = _currentTenant.Permissions ?? Unresolved;
        var roles       = _currentTenant.Roles       ?? Unresolved;

        // set_config(setting_name, value, is_local) — false = session-level
        cmd.CommandText = """
            SELECT
                set_config('app.current_brand_id',     @brand_id,     false),
                set_config('app.current_franchise_id', @franchise_id, false),
                set_config('app.current_store_id',     @store_id,     false),
                set_config('app.current_user_id',      @user_id,      false),
                set_config('app.current_partner_id',   @partner_id,   false),
                set_config('app.current_customer_id',  @customer_id,  false),
                set_config('app.current_user_type',    @user_type,    false),
                set_config('app.current_token_use',    @token_use,    false),
                set_config('app.current_scope_nodes',  @scope_nodes,  false),
                set_config('app.current_permissions',  @permissions,  false),
                set_config('app.current_roles',        @roles,        false),
                set_config('app.bypass_rls',           @bypass_rls,   false)
            """;

        AddParameter(cmd, "@brand_id",     brandId);
        AddParameter(cmd, "@franchise_id", franchiseId);
        AddParameter(cmd, "@store_id",     storeId);
        AddParameter(cmd, "@user_id",      userId);
        AddParameter(cmd, "@partner_id",   partnerId);
        AddParameter(cmd, "@customer_id",  customerId);
        AddParameter(cmd, "@user_type",    userType);
        AddParameter(cmd, "@token_use",    tokenUse);
        AddParameter(cmd, "@scope_nodes",  scopeNodes);
        AddParameter(cmd, "@permissions",  permissions);
        AddParameter(cmd, "@roles",        roles);
        AddParameter(cmd, "@bypass_rls",   bypassRls);
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
