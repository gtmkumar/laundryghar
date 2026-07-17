using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.Service;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.Service;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>Admin — catalog services. Per-route permission policies; brand scoping in handlers.</summary>
public class ServicesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/services";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Services")
             // Service edits regenerate the customer services read AND the published price-list
             // read (which projects the service name into each line's display label).
             .EvictOutputCacheOnWrite(CatalogCacheTags.Services, CatalogCacheTags.PriceList);

        // Admin list is brand-scoped (no per-user data); POS fetches it on cold launch. The
        // group's write filter above evicts catalog:services on any non-GET, so this stays fresh.
        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read")
            .CacheSharedOutput(CatalogCacheTags.Services, TimeSpan.FromMinutes(5), "page", "pageSize", "categoryId");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateServiceRequest>>()
            .RequireAuthorization("permission:catalog.service.create");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.service.update");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.service.delete");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? categoryId = null)
    {
        var r = await dispatcher.QueryAsync(new GetServicesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, categoryId), ct);
        return Results.Ok(new PaginatedListResponse<ServiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetServiceByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateServiceRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateServiceCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/services/{r.Id}",
            new SingleResponse<ServiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateServiceRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateServiceCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteServiceCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
