using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Pricing.Commands.ValueSlab;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Catalog.Pricing.Queries.ValueSlab;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — value-price slabs (GH #22). Branded/luxury garments are priced by declared garment
/// value; a brand authors its slabs here. Reads require <c>pricing.read</c>; writes require
/// <c>pricing.slab.manage</c>. Every write is recorded in the pricing change log (revertable).
/// </summary>
public class ValueSlabsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/pricing/value-slabs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Pricing - Value Slabs");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:pricing.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateValueSlabRequest>>()
            .RequireAuthorization("permission:pricing.slab.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdateValueSlabRequest>>()
            .RequireAuthorization("permission:pricing.slab.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:pricing.slab.manage");
    }

    public static async Task<IResult> GetAll(Guid? serviceId, bool? includeArchived,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetValueSlabsQuery(serviceId, includeArchived ?? false), ct);
        return Results.Ok(new ListResponse<ValueSlabDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> Create(CreateValueSlabRequest req, ICurrentUser u,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateValueSlabCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/pricing/value-slabs/{r.Id}",
            new SingleResponse<ValueSlabDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateValueSlabRequest req, ICurrentUser u,
        IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateValueSlabCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ValueSlabDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteValueSlabCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
