using laundryghar.Catalog.Application.Catalog.Commands;
using laundryghar.Catalog.Application.Catalog.Dtos;
using laundryghar.Catalog.Application.Catalog.Queries;
using MediatR;

namespace laundryghar.Catalog.Endpoints;

/// <summary>
/// Admin catalog management endpoints.
/// All require token_use=user + specific permission codes (or platform_admin bypass).
/// RLS scopes results to the current brand automatically via the interceptor.
/// </summary>
public static class AdminCatalogEndpoints
{
    public static RouteGroupBuilder MapAdminCatalogEndpoints(this RouteGroupBuilder group)
    {
        // ── Service Categories ───────────────────────────────────────────────
        var cats = group.MapGroup("/service-categories").WithTags("Admin - Catalog - Categories");

        cats.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetServiceCategoriesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<ServiceCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        cats.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetServiceCategoryByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        cats.MapPost("/", async (CreateServiceCategoryRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateServiceCategoryCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/service-categories/{r.Id}",
                new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.category.create");

        cats.MapPut("/{id:guid}", async (Guid id, UpdateServiceCategoryRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateServiceCategoryCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.category.update");

        cats.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteServiceCategoryCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.category.delete");

        // ── Services ─────────────────────────────────────────────────────────
        var svc = group.MapGroup("/services").WithTags("Admin - Catalog - Services");

        svc.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? categoryId = null) =>
        {
            var r = await sender.Send(new GetServicesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, categoryId), ct);
            return Results.Ok(new PaginatedListResponse<ServiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        svc.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetServiceByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        svc.MapPost("/", async (CreateServiceRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateServiceCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/services/{r.Id}",
                new SingleResponse<ServiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.service.create");

        svc.MapPut("/{id:guid}", async (Guid id, UpdateServiceRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateServiceCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.service.update");

        svc.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteServiceCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.service.delete");

        // ── Fabric Types ──────────────────────────────────────────────────────
        var fabrics = group.MapGroup("/fabric-types").WithTags("Admin - Catalog - Fabrics");

        fabrics.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetFabricTypesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<FabricTypeDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        fabrics.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetFabricTypeByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<FabricTypeDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        fabrics.MapPost("/", async (CreateFabricTypeRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateFabricTypeCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/fabric-types/{r.Id}",
                new SingleResponse<FabricTypeDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.fabric.manage");

        fabrics.MapPut("/{id:guid}", async (Guid id, UpdateFabricTypeRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateFabricTypeCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<FabricTypeDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.fabric.manage");

        fabrics.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteFabricTypeCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.fabric.manage");

        // ── Item Groups ────────────────────────────────────────────────────────
        var groups2 = group.MapGroup("/item-groups").WithTags("Admin - Catalog - Item Groups");

        groups2.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetItemGroupsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<ItemGroupDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        groups2.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetItemGroupByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemGroupDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        groups2.MapPost("/", async (CreateItemGroupRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateItemGroupCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/item-groups/{r.Id}",
                new SingleResponse<ItemGroupDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.itemgroup.manage");

        groups2.MapPut("/{id:guid}", async (Guid id, UpdateItemGroupRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateItemGroupCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemGroupDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.itemgroup.manage");

        groups2.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteItemGroupCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.itemgroup.manage");

        // ── Items ─────────────────────────────────────────────────────────────
        var items = group.MapGroup("/items").WithTags("Admin - Catalog - Items");

        items.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? itemGroupId = null) =>
        {
            var r = await sender.Send(new GetItemsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, itemGroupId), ct);
            return Results.Ok(new PaginatedListResponse<ItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        items.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetItemByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        items.MapPost("/", async (CreateItemRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateItemCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/items/{r.Id}",
                new SingleResponse<ItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.item.create");

        items.MapPut("/{id:guid}", async (Guid id, UpdateItemRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateItemCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.item.update");

        items.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteItemCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.item.delete");

        // ── Item Image ────────────────────────────────────────────────────────
        // POST /api/v1/admin/items/{id}/image
        // Multipart upload: IFormFile requires DisableAntiforgery() in .NET 10 minimal APIs.
        items.MapPost("/{id:guid}/image", async (
            Guid id, IFormFile file, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UploadItemImageCommand(id, file, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
        })
        .RequireAuthorization("permission:catalog.item.update")
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(6 * 1024 * 1024)); // 5 MB + envelope overhead

        // GET /api/v1/admin/items/{id}/image — streams the image
        // Streaming by item ID avoids exposing raw storage keys in the API surface.
        items.MapGet("/{id:guid}/image", async (Guid id, [FromServices] ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetItemImageStreamQuery(id), ct);
            if (result is null) return Results.NotFound();

            return Results.Stream(
                result.Stream,
                contentType: result.ContentType,
                fileDownloadName: result.FileName,
                enableRangeProcessing: false);
        }).RequireAuthorization("permission:catalog.read");

        // DELETE /api/v1/admin/items/{id}/image
        items.MapDelete("/{id:guid}/image", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new DeleteItemImageCommand(id, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.item.update");

        // ── Item Variants ─────────────────────────────────────────────────────
        var variants = group.MapGroup("/item-variants").WithTags("Admin - Catalog - Variants");

        variants.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, Guid? itemId = null) =>
        {
            var r = await sender.Send(new GetItemVariantsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, itemId), ct);
            return Results.Ok(new PaginatedListResponse<ItemVariantDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        variants.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetItemVariantByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemVariantDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        variants.MapPost("/", async (CreateItemVariantRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateItemVariantCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/item-variants/{r.Id}",
                new SingleResponse<ItemVariantDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.variant.manage");

        variants.MapPut("/{id:guid}", async (Guid id, UpdateItemVariantRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateItemVariantCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemVariantDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.variant.manage");

        variants.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteItemVariantCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.variant.manage");

        // ── Add-Ons ───────────────────────────────────────────────────────────
        var addons = group.MapGroup("/add-ons").WithTags("Admin - Catalog - Add-Ons");

        addons.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetAddOnsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<AddOnDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        addons.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetAddOnByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AddOnDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.read");

        addons.MapPost("/", async (CreateAddOnRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateAddOnCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/add-ons/{r.Id}",
                new SingleResponse<AddOnDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.addon.manage");

        addons.MapPut("/{id:guid}", async (Guid id, UpdateAddOnRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateAddOnCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AddOnDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:catalog.addon.manage");

        addons.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteAddOnCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:catalog.addon.manage");

        return group;
    }
}
