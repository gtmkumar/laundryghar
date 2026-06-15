using core.Application.Identity.AccessControl.Commands.InviteRider;
using core.Application.Identity.AccessControl.Commands.InviteUser;
using core.Application.Identity.AccessControl.Commands.SetPersonStatus;
using core.Application.Identity.AccessControl.Commands.SetRoleCell;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.AccessControl.Queries.GetAccessFranchises;
using core.Application.Identity.AccessControl.Queries.GetAccessPeople;
using core.Application.Identity.AccessControl.Queries.GetAccessRoles;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Access Control console (/api/v1/admin/access-control): People / Roles &amp; Permissions /
/// Franchises tabs plus invite + status writes. Privilege-escalation guards live in the handlers.
/// </summary>
public class AdminAccessControl : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/access-control";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Access Control").RequireAuthorization();

        group.MapGet(GetAccessPeople, "people").RequireAuthorization("permission:users.list");
        group.MapGet(GetAccessRoles, "roles").RequireAuthorization("permission:roles.list");
        group.MapGet(GetAccessFranchises, "franchises").RequireAuthorization("permission:franchises.list");
        group.MapPost(InviteUser, "invite").RequireAuthorization("permission:users.create");
        // Narrow rider-invite: franchise-scoped actors can onboard their own riders.
        group.MapPost(InviteRider, "riders/invite").RequireAuthorization("permission:rider.manage");
        group.MapPost(SetRoleCell, "role-cell").RequireAuthorization("permission:permissions.assign");
        group.MapPost(SetPersonStatus, "people/{id:guid}/status").RequireAuthorization("permission:users.update");
    }

    public static async Task<IResult> GetAccessPeople(string? search, Guid? franchiseId, string? sort,
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 100)
    {
        var data = await dispatcher.QueryAsync(
            new GetAccessPeopleQuery(search, page < 1 ? 1 : page, pageSize < 1 ? 100 : pageSize, franchiseId, sort), ct);
        return Results.Ok(new SingleResponse<AccessPeoplePageDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetAccessRoles(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetAccessRolesQuery(), ct);
        return Results.Ok(new SingleResponse<AccessRolesDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetAccessFranchises(string? search, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 100)
    {
        var data = await dispatcher.QueryAsync(
            new GetAccessFranchisesQuery(page < 1 ? 1 : page, pageSize < 1 ? 100 : pageSize, search), ct);
        return Results.Ok(new PaginatedListResponse<FranchiseCardDto> { Status = true, Data = data });
    }

    public static async Task<IResult> InviteUser(InviteUserRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new InviteUserCommand(req), ct);
        return Results.Ok(new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> InviteRider(InviteRiderRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new InviteRiderCommand(req), ct);
        return Results.Ok(new SingleResponse<UserDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SetRoleCell(SetRoleCellRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SetRoleCellCommand(req, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> SetPersonStatus(Guid id, SetPersonStatusRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new SetPersonStatusCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<SetPersonStatusResult> { Status = true, Data = data });
    }
}
