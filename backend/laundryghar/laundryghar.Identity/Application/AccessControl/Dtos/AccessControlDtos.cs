namespace laundryghar.Identity.Application.AccessControl.Dtos;

// ── People tab ──────────────────────────────────────────────────────────────
public sealed record PersonDto(
    Guid Id, string Name, string Email, string Initials,
    string RoleCode, string RoleName, string ScopeLabel,
    string Tier, string Status, DateTimeOffset? LastActiveAt);

public sealed record PeopleCountsDto(int All, int HqEmployees, int FranchiseOwners, int FranchiseStaff);

public sealed record AccessPeopleDto(PeopleCountsDto Counts, IReadOnlyList<PersonDto> People);

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

/// <summary>Toggle a whole matrix cell (assigns/removes all permissions it maps to).</summary>
public sealed record SetRoleCellRequest(Guid RoleId, string CellKey, bool Enabled);

// ── Navigator (data-driven sidebar menu, gated by the user's permissions) ────
public sealed record NavItemDto(string Key, string Label, string? Icon, string? Route);
public sealed record NavSectionDto(string Section, IReadOnlyList<NavItemDto> Items);
public sealed record NavigatorDto(IReadOnlyList<NavSectionDto> Sections);
