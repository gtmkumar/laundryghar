using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.ItemVariant;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.ItemVariant;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>Admin — catalog item variants. All mutations gated by permission:catalog.variant.manage.</summary>
public class ItemVariantsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/item-variants";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Variants");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateItemVariantRequest>>()
            .RequireAuthorization("permission:catalog.variant.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.variant.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.variant.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? itemId = null)
    {
        var r = await dispatcher.QueryAsync(new GetItemVariantsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, itemId), ct);
        return Results.Ok(new PaginatedListResponse<ItemVariantDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetItemVariantByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemVariantDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateItemVariantRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateItemVariantCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/item-variants/{r.Id}",
            new SingleResponse<ItemVariantDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateItemVariantRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateItemVariantCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ItemVariantDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteItemVariantCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
