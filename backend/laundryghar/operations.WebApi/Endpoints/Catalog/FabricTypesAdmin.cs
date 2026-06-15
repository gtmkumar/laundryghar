using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.FabricType;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.FabricType;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>Admin — catalog fabric types. All mutations gated by permission:catalog.fabric.manage.</summary>
public class FabricTypesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/fabric-types";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Fabrics");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateFabricTypeRequest>>()
            .RequireAuthorization("permission:catalog.fabric.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.fabric.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.fabric.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetFabricTypesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<FabricTypeDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetFabricTypeByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<FabricTypeDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateFabricTypeRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateFabricTypeCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/fabric-types/{r.Id}",
            new SingleResponse<FabricTypeDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateFabricTypeRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateFabricTypeCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<FabricTypeDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteFabricTypeCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
