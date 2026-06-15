using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Platforms.Commands.CreatePlatform;
using core.Application.Identity.TenancyOrg.Platforms.Queries.GetPlatformById;
using core.Application.Identity.TenancyOrg.Platforms.Queries.GetPlatforms;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — platform-level Platform CRUD (top of the org hierarchy). Thin: each method dispatches a
/// command/query through <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class AdminPlatforms : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/platforms";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Platforms").RequireAuthorization();

        group.MapGet(GetAll).RequireAuthorization("permission:platforms.list");
        group.MapGet(GetById, "{id:guid}").RequireAuthorization("permission:platforms.list");
        group.MapPost(Create).RequireAuthorization("permission:platforms.create");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(new GetPlatformsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PlatformDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetPlatformByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PlatformDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreatePlatformCommand cmd, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(cmd, ct);
        return Results.Created($"/api/v1/admin/platforms/{data.Id}",
            new SingleResponse<PlatformDto> { Status = true, Data = data });
    }
}
