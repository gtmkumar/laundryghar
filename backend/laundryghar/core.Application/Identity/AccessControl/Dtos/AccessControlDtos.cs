namespace core.Application.Identity.AccessControl.Dtos;

// ── People tab ──────────────────────────────────────────────────────────────
public sealed record PersonDto(
    Guid Id, string Name, string Email, string Initials,
    string RoleCode, string RoleName, string ScopeLabel,
    string Tier, string Status, DateTimeOffset? LastActiveAt);

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
    IReadOnlyList<string> OnCells); // "module:action" cells that are enabled

public sealed record RoleGroupDto(string Tier, string TierLabel, IReadOnlyList<RoleSummaryDto> Roles);

public sealed record AccessRolesDto(
    IReadOnlyList<MatrixModuleDto> Modules,
    IReadOnlyList<string> Actions,
    IReadOnlyList<RoleGroupDto> Groups);

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

/// <summary>Toggle a whole matrix cell (assigns/removes all permissions it maps to).</summary>
public sealed record SetRoleCellRequest(Guid RoleId, string CellKey, bool Enabled);

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
