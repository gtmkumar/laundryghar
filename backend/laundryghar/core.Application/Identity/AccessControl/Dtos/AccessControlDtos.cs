namespace core.Application.Identity.AccessControl.Dtos;

// ── People tab ──────────────────────────────────────────────────────────────
public sealed record PersonDto(
    Guid Id, string Name, string Email, string Initials,
    string RoleCode, string RoleName, string ScopeLabel,
    string Tier, string Status,
    // The account's coarse user_type (e.g. ops_staff / warehouse_staff). The role *code* alone
    // can't distinguish the vertical-neutral ops_staff (it shares role codes), so the directory
    // needs the type to badge and humanise it. See SharedDataModel/Enums/UserType.
    string UserType,
    DateTimeOffset? LastActiveAt);

public sealed record PeopleCountsDto(int All, int HqEmployees, int FranchiseOwners, int FranchiseStaff);

public sealed record AccessPeopleDto(PeopleCountsDto Counts, IReadOnlyList<PersonDto> People);

/// <summary>Paged people response: aggregate counts (full set) + the current page of people.</summary>
public sealed record AccessPeoplePageDto(
    PeopleCountsDto Counts,
    laundryghar.Utilities.Common.PaginatedList<PersonDto> People);

// ── Roles & Permissions tab ─────────────────────────────────────────────────
public sealed record MatrixModuleDto(string Key, string Label);

public sealed record RoleSummaryDto(
    Guid Id, string Code, string Name, string? Description,
    string ScopeType, bool IsSystem, int MemberCount,
    IReadOnlyList<string> OnCells, // "module:action" cells that are enabled
    // Vertical the role belongs to (laundry/salon/logistics), or null = neutral/all brands.
    string? VerticalKey = null);

public sealed record RoleGroupDto(string Tier, string TierLabel, IReadOnlyList<RoleSummaryDto> Roles);

public sealed record AccessRolesDto(
    IReadOnlyList<MatrixModuleDto> Modules,
    IReadOnlyList<string> Actions,
    IReadOnlyList<RoleGroupDto> Groups,
    // cellKey ("module:action") → the permission codes that cell grants (for the UI fan-out tooltip).
    IReadOnlyDictionary<string, IReadOnlyList<string>> Cells);

// ── Franchises tab ──────────────────────────────────────────────────────────
public sealed record FranchiseCardDto(
    Guid Id, string Name, string OwnershipType, string Location, int SinceYear,
    string? OwnerName, string? OwnerInitials,
    int StoreCount, int StaffCount, int RiderCount, long RevenueMonthly, string Status);

public sealed record AccessFranchisesDto(IReadOnlyList<FranchiseCardDto> Franchises);

// ── Write payloads ──────────────────────────────────────────────────────────
/// <summary>Invite = create user + grant a primary role within a scope.</summary>
public sealed record InviteUserRequest(
    string Email, string? Phone, string? FirstName, string? LastName,
    string UserType, Guid RoleId, string ScopeType, Guid? ScopeId, string? Password);

/// <summary>
/// Invite a rider into a specific franchise. Requires <c>permission:rider.manage</c>.
/// Franchise-scoped actors (franchise_owner) must omit or will have FranchiseId overridden
/// to their own franchise. Brand/platform admins supply FranchiseId explicitly.
/// </summary>
public sealed record InviteRiderRequest(
    string Email, string? Phone, string? FirstName, string? LastName, Guid FranchiseId);

/// <summary>One cell change in a batch save.</summary>
public sealed record RoleCellChange(string CellKey, bool Enabled);
/// <summary>Apply many cell changes to one role atomically (single transaction).</summary>
public sealed record SetRoleCellsRequest(IReadOnlyList<RoleCellChange> Changes);

/// <summary>
/// Change a person's account status. <c>Action</c> is one of
/// <c>activate</c> (invited → active, sets the temp password),
/// <c>suspend</c> (active → suspended) or <c>reactivate</c> (suspended → active).
/// </summary>
public sealed record SetPersonStatusRequest(string Action, string? Password);

/// <summary>Result of a status change — the new status plus whether a first-login reset is required.</summary>
public sealed record SetPersonStatusResult(string Status, bool MustChangePassword);

// ── Navigator (data-driven sidebar menu, gated by the user's permissions) ────
public sealed record NavItemDto(string Key, string Label, string? Icon, string? Route);
public sealed record NavSectionDto(string Section, IReadOnlyList<NavItemDto> Items);
public sealed record NavigatorDto(IReadOnlyList<NavSectionDto> Sections);

/// <summary>
/// One live scope membership, as the person drawer reads it (audit A-4). Carries the resolved scope
/// NAME as well as its id: the panel names a scope without having to hold every franchise, store and
/// warehouse list in memory to look one up.
/// </summary>
public sealed record PersonMembershipDto(
    Guid Id,
    Guid UserId,
    string ScopeType,
    Guid? ScopeId,
    string? ScopeName,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    bool IsPrimary,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// One live per-user permission override, as the person drawer reads it (audit A-4 / ABAC A8.2).
/// Carries the permission's display name and module so the panel need not re-look them up in the
/// catalogue, and the resolved scope name for a scoped override.
/// </summary>
public sealed record PersonPermissionOverrideDto(
    Guid Id,
    Guid UserId,
    string PermissionCode,
    string PermissionName,
    string Module,
    string Effect,
    string? ScopeType,
    Guid? ScopeId,
    string? ScopeName,
    string? Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset GrantedAt);
