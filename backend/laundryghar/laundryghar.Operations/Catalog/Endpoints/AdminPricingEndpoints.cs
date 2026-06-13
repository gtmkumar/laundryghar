using laundryghar.Catalog.Application.Pricing.Commands;
using laundryghar.Catalog.Application.Pricing.Dtos;
using laundryghar.Catalog.Application.Pricing.Queries;
using MediatR;

namespace laundryghar.Catalog.Endpoints;

/// <summary>
/// Admin pricing management endpoints.
/// Price lists: draft → publish lifecycle with parent inheritance support.
/// Resolve endpoint returns the effective price via store → franchise → brand fallback.
/// </summary>
public static class AdminPricingEndpoints
{
    public static RouteGroupBuilder MapAdminPricingEndpoints(this RouteGroupBuilder group)
    {
        // ── Price Lists ───────────────────────────────────────────────────────
        var lists = group.MapGroup("/price-lists").WithTags("Admin - Pricing - Price Lists");

        lists.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetPriceListsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PriceListDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.read");

        lists.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetPriceListByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.read");

        lists.MapPost("/", async (CreatePriceListRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePriceListCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/price-lists/{r.Id}",
                new SingleResponse<PriceListDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.pricelist.create");

        lists.MapPut("/{id:guid}", async (Guid id, UpdatePriceListRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdatePriceListCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.pricelist.update");

        lists.MapPost("/{id:guid}/publish", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new PublishPriceListCommand(id, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.pricelist.publish");

        lists.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeletePriceListCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:pricing.pricelist.update");

        // ── Price List Items ──────────────────────────────────────────────────
        var items = group.MapGroup("/price-lists/{priceListId:guid}/items").WithTags("Admin - Pricing - Items");

        items.MapGet("/", async (
            Guid priceListId, [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 50) =>
        {
            var r = await sender.Send(new GetPriceListItemsQuery(priceListId, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<PriceListItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.read");

        items.MapPost("/", async (Guid priceListId, CreatePriceListItemRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreatePriceListItemCommand(priceListId, req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/price-lists/{priceListId}/items/{r.Id}",
                new SingleResponse<PriceListItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.item.manage");

        items.MapPut("/{id:guid}", async (Guid priceListId, Guid id, UpdatePriceListItemRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdatePriceListItemCommand(priceListId, id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PriceListItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.item.manage");

        // ── Price Resolution ──────────────────────────────────────────────────
        var pricing = group.MapGroup("/pricing").WithTags("Admin - Pricing - Resolve");

        pricing.MapGet("/resolve", async (Guid itemId, Guid serviceId, Guid? variantId, Guid? storeId,
            ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new ResolvePriceQuery(itemId, serviceId, variantId, storeId), ct);
            return r is null
                ? Results.NotFound(new Response { Status = false })
                : Results.Ok(new SingleResponse<PriceResolutionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:pricing.read");

        return group;
    }
}
