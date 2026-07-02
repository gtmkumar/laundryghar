using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Pricing.Commands.PriceList;
using operations.Application.Catalog.Pricing.Commands.PriceListItem;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Catalog.Pricing.Queries.PriceLists;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — pricing price lists (draft → publish lifecycle) and their items.
/// Price-list items live under /price-lists/{priceListId}/items.
/// </summary>
public class PriceListsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/price-lists";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Pricing - Price Lists");

        // Price lists
        group.MapGet(GetAll, "/").RequireAuthorization("permission:pricing.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:pricing.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreatePriceListRequest>>()
            .RequireAuthorization("permission:pricing.pricelist.create");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:pricing.pricelist.update");
        group.MapPost(Publish, "/{id:guid}/publish").RequireAuthorization("permission:pricing.pricelist.publish");
        group.MapGet(Export, "/{id:guid}/export").RequireAuthorization("permission:pricing.read");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:pricing.pricelist.update");

        // Price-list items
        group.MapGet(GetItems, "/{priceListId:guid}/items").RequireAuthorization("permission:pricing.read");
        group.MapPost(CreateItem, "/{priceListId:guid}/items")
            .AddEndpointFilter<ValidationFilter<CreatePriceListItemRequest>>()
            .RequireAuthorization("permission:pricing.item.manage");
        group.MapPut(UpdateItem, "/{priceListId:guid}/items/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdatePriceListItemRequest>>()
            .RequireAuthorization("permission:pricing.item.manage");
    }

    // ── Price lists ───────────────────────────────────────────────────────────

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetPriceListsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PriceListDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPriceListByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreatePriceListRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePriceListCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/price-lists/{r.Id}",
            new SingleResponse<PriceListDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdatePriceListRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePriceListCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Publish(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new PublishPriceListCommand(id, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeletePriceListCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    // Export the list to the flat import template (round-trips with the import pipeline). format=csv|xlsx.
    public static async Task<IResult> Export(Guid id, IDispatcher dispatcher, CancellationToken ct, string format = "csv")
    {
        var r = await dispatcher.QueryAsync(new ExportPriceListQuery(id, format), ct);
        return r is null ? Results.NotFound() : Results.File(r.Content, r.ContentType, r.FileName);
    }

    // ── Price-list items ──────────────────────────────────────────────────────

    public static async Task<IResult> GetItems(Guid priceListId, IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 50)
    {
        var r = await dispatcher.QueryAsync(new GetPriceListItemsQuery(priceListId, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PriceListItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> CreateItem(Guid priceListId, CreatePriceListItemRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePriceListItemCommand(priceListId, req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/price-lists/{priceListId}/items/{r.Id}",
            new SingleResponse<PriceListItemDto> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateItem(Guid priceListId, Guid id, UpdatePriceListItemRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePriceListItemCommand(priceListId, id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListItemDto> { Status = true, Data = r });
    }
}
