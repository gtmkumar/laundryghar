namespace laundryghar.SharedDataModel.Enums;

/// <summary>Scope type values used in operating_hours, holidays, user_scope_memberships, system_settings, etc.</summary>
public static class ScopeType
{
    public const string Platform = "platform";
    public const string Brand = "brand";
    public const string Franchise = "franchise";
    public const string Store = "store";
    public const string Warehouse = "warehouse";
    public const string Territory = "territory";

    /// <summary>
    /// RaaS (Rider-as-a-Service) external logistics partner scope. Used only by the
    /// system roles partner_admin / partner_operator so the RBAC catalog + role-management
    /// UI are truthful (see docs/rbac.md §4/§10). NOTE: the partner MVP enforces these roles
    /// via a partner_role JWT claim + partner_id RLS — NOT role_permissions/ScopeResolver — so
    /// partners never receive user_scope_memberships rows (which would require staff users).
    /// </summary>
    public const string LogisticsPartner = "logistics_partner";
}
