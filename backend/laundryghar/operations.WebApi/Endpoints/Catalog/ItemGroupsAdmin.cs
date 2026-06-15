using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.ItemGroup;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.ItemGroup;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>Admin — catalog item groups. All mutations gated by permission:catalog.itemgroup.manage.</summary>
public class ItemGroupsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/item-groups";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Item Groups");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateItemGroupRequest>>()
            .RequireAuthorization("permission:catalog.itemgroup.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.itemgroup.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.itemgroup.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetItemGroupsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<ItemGroupDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetItemGroupByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemGroupDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateItemGroupRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateItemGroupCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/item-groups/{r.Id}",
            new SingleResponse<ItemGroupDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateItemGroupRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateItemGroupCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemGroupDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteItemGroupCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
