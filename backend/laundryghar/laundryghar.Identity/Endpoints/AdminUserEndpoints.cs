using laundryghar.Identity.Application.AccessControl.Commands;
using laundryghar.Identity.Application.AccessControl.Dtos;
using laundryghar.Identity.Application.AccessControl.Queries;
using laundryghar.Identity.Application.Users.Commands;
using laundryghar.Identity.Infrastructure.Services;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// /api/v1/admin/users — User management
/// /api/v1/admin/roles — Roles & permissions
/// </summary>
public static class AdminUserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        // Users
        var users = group.MapGroup("/users").WithTags("Admin - Users").RequireAuthorization();

        users.MapGet("/", async (int page, int pageSize, string? status, string? userType, string? search,
            ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetUsersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, userType, search), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<UserDto>
                { Status = true, Data = r });
        }).WithName("GetUsers").RequireAuthorization("permission:users.list");

        users.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetUserByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<UserDto> { Status = true, Data = r });
        }).WithName("GetUserById").RequireAuthorization("permission:users.read");

        users.MapPost("/", async (CreateUserRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateUserCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/users/{r.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<UserDto> { Status = true, Data = r });
        }).WithName("CreateUser").RequireAuthorization("permission:users.create");

        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateUserCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<UserDto> { Status = true, Data = r });
        }).WithName("UpdateUser").RequireAuthorization("permission:users.update");

        users.MapPost("/{id:guid}/deactivate", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeactivateUserCommand(id, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("DeactivateUser").RequireAuthorization("permission:users.deactivate");

        users.MapPost("/{id:guid}/set-password", async (Guid id, SetPasswordRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new SetPasswordCommand(id, req, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("SetUserPassword").RequireAuthorization("permission:users.set_password");

        // H3: Separate privileged endpoint for changing user_type
        users.MapPost("/{id:guid}/set-type", async (Guid id, SetUserTypeRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new SetUserTypeCommand(id, req, u), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("SetUserType").RequireAuthorization("permission:users.set_type");

        // Roles & permissions
        var roles = group.MapGroup("/roles").WithTags("Admin - Roles").RequireAuthorization();

        roles.MapGet("/", async (int page, int pageSize, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetRolesQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.ListResponse<RoleDto> { Status = true, Data = r });
        }).WithName("GetRoles").RequireAuthorization("permission:roles.list");

        roles.MapGet("/permissions", async (string? module, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPermissionsQuery(module), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.ListResponse<PermissionDto> { Status = true, Data = r });
        }).WithName("GetPermissions").RequireAuthorization("permission:permissions.list");

        roles.MapPost("/assign-permission", async (AssignPermissionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new AssignPermissionCommand(req, u.UserId), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true });
        }).WithName("AssignPermission").RequireAuthorization("permission:permissions.assign");

        roles.MapPost("/memberships/grant", async (GrantMembershipRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GrantMembershipCommand(req, u.UserId, u), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<MembershipDto> { Status = true, Data = r });
        }).WithName("GrantMembership").RequireAuthorization("permission:memberships.grant");

        roles.MapPost("/memberships/revoke", async (RevokeMembershipRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new RevokeMembershipCommand(req, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("RevokeMembership").RequireAuthorization("permission:memberships.revoke");

        // Data-driven navigator — the signed-in user's sidebar menu.
        group.MapGet("/navigator", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetNavigatorQuery(), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<NavigatorDto> { Status = true, Data = r });
        }).WithName("GetNavigator").RequireAuthorization();

        // ── Access Control console (People / Roles & Permissions / Franchises) ──
        var ac = group.MapGroup("/access-control").WithTags("Admin - Access Control").RequireAuthorization();

        ac.MapGet("/people", async (string? search, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAccessPeopleQuery(search), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<AccessPeopleDto> { Status = true, Data = r });
        }).WithName("GetAccessPeople").RequireAuthorization("permission:users.list");

        ac.MapGet("/roles", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAccessRolesQuery(), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<AccessRolesDto> { Status = true, Data = r });
        }).WithName("GetAccessRoles").RequireAuthorization("permission:roles.list");

        ac.MapGet("/franchises", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAccessFranchisesQuery(), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<AccessFranchisesDto> { Status = true, Data = r });
        }).WithName("GetAccessFranchises").RequireAuthorization("permission:franchises.list");

        ac.MapPost("/invite", async (InviteUserRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new InviteUserCommand(req, u), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<UserDto> { Status = true, Data = r });
        }).WithName("InviteUser").RequireAuthorization("permission:users.create");

        ac.MapPost("/role-cell", async (SetRoleCellRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new SetRoleCellCommand(req, u.UserId), ct);
            return ok ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("SetRoleCell").RequireAuthorization("permission:permissions.assign");

        return group;
    }
}
