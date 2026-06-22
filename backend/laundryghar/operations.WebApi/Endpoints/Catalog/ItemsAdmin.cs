using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.Item;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — catalog items + item image (upload / stream / delete). Image is served by item id,
/// never by raw storage key. Multipart upload requires DisableAntiforgery() in .NET 10 minimal APIs.
/// </summary>
public class ItemsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/items";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Items");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetManaged, "/managed").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetStats, "/stats").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPut(SavePricing, "/{id:guid}/pricing").RequireAuthorization("permission:pricing.item.manage");
        group.MapPost(Import, "/import").RequireAuthorization("permission:catalog.item.create");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateItemRequest>>()
            .RequireAuthorization("permission:catalog.item.create");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.item.update");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.item.delete");

        // Item image — POST upload / GET stream / DELETE
        group.MapPost(UploadImage, "/{id:guid}/image")
            .RequireAuthorization("permission:catalog.item.update")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024)); // 5 MB + envelope overhead
        group.MapGet(GetImage, "/{id:guid}/image").RequireAuthorization("permission:catalog.read");
        group.MapDelete(DeleteImage, "/{id:guid}/image").RequireAuthorization("permission:catalog.item.update");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? itemGroupId = null)
    {
        var r = await dispatcher.QueryAsync(new GetItemsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, itemGroupId), ct);
        return Results.Ok(new PaginatedListResponse<ItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetManaged(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 100, Guid? itemGroupId = null, string? search = null)
    {
        var r = await dispatcher.QueryAsync(
            new GetManagedItemsQuery(page < 1 ? 1 : page, pageSize < 1 ? 100 : pageSize, itemGroupId, search), ct);
        return Results.Ok(new PaginatedListResponse<ManagedItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetStats(IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetItemStatsQuery(), ct);
        return Results.Ok(new SingleResponse<ItemStatsDto> { Status = true, Data = r });
    }

    public static async Task<IResult> SavePricing(Guid id, SaveItemPricingRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new SaveItemPricingCommand(id, req, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> Import(ImportItemsRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ImportItemsCommand(req, u.UserId), ct);
        return Results.Ok(new SingleResponse<ImportItemsResult> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetItemByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateItemRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateItemCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/items/{r.Id}",
            new SingleResponse<ItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateItemRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateItemCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteItemCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> UploadImage(Guid id, IFormFile file, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UploadItemImageCommand(id, file, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetImage(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.QueryAsync(new GetItemImageStreamQuery(id), ct);
        if (result is null) return Results.NotFound();

        return Results.Stream(
            result.Stream,
            contentType: result.ContentType,
            fileDownloadName: result.FileName,
            enableRangeProcessing: false);
    }

    public static async Task<IResult> DeleteImage(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new DeleteItemImageCommand(id, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
    }
}
