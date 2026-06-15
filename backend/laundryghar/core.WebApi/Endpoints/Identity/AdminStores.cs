using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Stores.Commands.CreateStore;
using core.Application.Identity.TenancyOrg.Stores.Commands.DeleteStore;
using core.Application.Identity.TenancyOrg.Stores.Commands.UpdateStore;
using core.Application.Identity.TenancyOrg.Stores.Queries.GetStoreById;
using core.Application.Identity.TenancyOrg.Stores.Queries.GetStores;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — Store CRUD. List filters by brandId + franchiseId. Thin: each method dispatches a
/// command/query through <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class AdminStores : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/stores";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Stores").RequireAuthorization();

        group.MapGet(GetAll).RequireAuthorization("permission:stores.list");
        group.MapGet(GetById, "{id:guid}").RequireAuthorization("permission:stores.read");
        group.MapPost(Create).RequireAuthorization("permission:stores.create");
        group.MapPut(Update, "{id:guid}").RequireAuthorization("permission:stores.update");
        group.MapDelete(Delete, "{id:guid}").RequireAuthorization("permission:stores.delete");
    }

    public static async Task<IResult> GetAll(Guid? brandId, Guid? franchiseId, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(new GetStoresQuery(brandId, franchiseId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<StoreDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetStoreByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<StoreDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateStoreRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateStoreCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/stores/{data.Id}",
            new SingleResponse<StoreDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateStoreRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateStoreCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<StoreDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteStoreCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
