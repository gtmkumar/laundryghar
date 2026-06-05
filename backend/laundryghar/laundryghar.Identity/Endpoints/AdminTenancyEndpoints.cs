using laundryghar.Identity.Application.TenancyOrg.Commands;
using laundryghar.Identity.Infrastructure.Services;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// Org hierarchy CRUD:
///   /platforms, /franchises, /stores, /warehouses
/// </summary>
public static class AdminTenancyEndpoints
{
    public static RouteGroupBuilder MapTenancyEndpoints(this RouteGroupBuilder group)
    {
        // Platforms
        var platforms = group.MapGroup("/platforms").WithTags("Admin - Platforms").RequireAuthorization();

        platforms.MapGet("/", async (int page, int pageSize, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPlatformsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<PlatformDto>
                { Status = true, Data = r });
        })
        .WithName("GetPlatforms").RequireAuthorization("permission:platforms.list");

        platforms.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPlatformByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<PlatformDto> { Status = true, Data = r });
        }).WithName("GetPlatformById").RequireAuthorization("permission:platforms.list");

        platforms.MapPost("/", async (CreatePlatformCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/admin/platforms/{r.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<PlatformDto> { Status = true, Data = r });
        }).WithName("CreatePlatform").RequireAuthorization("permission:platforms.create");

        // Franchises
        var franchises = group.MapGroup("/franchises").WithTags("Admin - Franchises").RequireAuthorization();

        franchises.MapGet("/", async (Guid? brandId, int page, int pageSize, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetFranchisesQuery(brandId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<FranchiseDto> { Status = true, Data = r });
        }).WithName("GetFranchises").RequireAuthorization("permission:franchises.list");

        franchises.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetFranchiseByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<FranchiseDto> { Status = true, Data = r });
        }).WithName("GetFranchiseById").RequireAuthorization("permission:franchises.read");

        franchises.MapPost("/", async (CreateFranchiseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateFranchiseCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/franchises/{r.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<FranchiseDto> { Status = true, Data = r });
        }).WithName("CreateFranchise").RequireAuthorization("permission:franchises.create");

        franchises.MapPut("/{id:guid}", async (Guid id, UpdateFranchiseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateFranchiseCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<FranchiseDto> { Status = true, Data = r });
        }).WithName("UpdateFranchise").RequireAuthorization("permission:franchises.update");

        franchises.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeleteFranchiseCommand(id, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("DeleteFranchise").RequireAuthorization("permission:franchises.delete");

        // Stores
        var stores = group.MapGroup("/stores").WithTags("Admin - Stores").RequireAuthorization();

        stores.MapGet("/", async (Guid? brandId, Guid? franchiseId, int page, int pageSize, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetStoresQuery(brandId, franchiseId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<StoreDto> { Status = true, Data = r });
        }).WithName("GetStores").RequireAuthorization("permission:stores.list");

        stores.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetStoreByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<StoreDto> { Status = true, Data = r });
        }).WithName("GetStoreById").RequireAuthorization("permission:stores.read");

        stores.MapPost("/", async (CreateStoreRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateStoreCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/stores/{r.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<StoreDto> { Status = true, Data = r });
        }).WithName("CreateStore").RequireAuthorization("permission:stores.create");

        stores.MapPut("/{id:guid}", async (Guid id, UpdateStoreRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateStoreCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<StoreDto> { Status = true, Data = r });
        }).WithName("UpdateStore").RequireAuthorization("permission:stores.update");

        stores.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeleteStoreCommand(id, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("DeleteStore").RequireAuthorization("permission:stores.delete");

        // Warehouses
        var warehouses = group.MapGroup("/warehouses").WithTags("Admin - Warehouses").RequireAuthorization();

        warehouses.MapGet("/", async (Guid? brandId, Guid? franchiseId, int page, int pageSize, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetWarehousesQuery(brandId, franchiseId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<WarehouseDto> { Status = true, Data = r });
        }).WithName("GetWarehouses").RequireAuthorization("permission:warehouses.list");

        warehouses.MapPost("/", async (CreateWarehouseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateWarehouseCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/warehouses/{r.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<WarehouseDto> { Status = true, Data = r });
        }).WithName("CreateWarehouse").RequireAuthorization("permission:warehouses.create");

        warehouses.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeleteWarehouseCommand(id, u.UserId), ct);
            return r ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true }) : Results.NotFound();
        }).WithName("DeleteWarehouse").RequireAuthorization("permission:warehouses.delete");

        return group;
    }
}
