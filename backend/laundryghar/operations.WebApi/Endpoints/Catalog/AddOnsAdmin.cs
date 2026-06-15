using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.AddOn;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.AddOn;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>Admin — catalog add-ons. All mutations gated by permission:catalog.addon.manage.</summary>
public class AddOnsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/add-ons";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Add-Ons");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateAddOnRequest>>()
            .RequireAuthorization("permission:catalog.addon.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.addon.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.addon.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetAddOnsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<AddOnDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetAddOnByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AddOnDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateAddOnRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateAddOnCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/add-ons/{r.Id}",
            new SingleResponse<AddOnDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateAddOnRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateAddOnCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AddOnDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteAddOnCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
