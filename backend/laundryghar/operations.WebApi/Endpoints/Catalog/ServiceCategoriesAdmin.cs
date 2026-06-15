using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Catalog.Commands.ServiceCategory;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Queries.ServiceCategory;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — catalog service categories. Per-route permission policies (or platform_admin bypass).
/// Brand scoping enforced in handlers via ICurrentUser.RequireBrandId().
/// </summary>
public class ServiceCategoriesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/service-categories";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Catalog - Categories");

        group.MapGet(GetAll, "/").RequireAuthorization("permission:catalog.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:catalog.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateServiceCategoryRequest>>()
            .RequireAuthorization("permission:catalog.category.create");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:catalog.category.update");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:catalog.category.delete");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetServiceCategoriesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<ServiceCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetServiceCategoryByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateServiceCategoryRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateServiceCategoryCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/service-categories/{r.Id}",
            new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateServiceCategoryRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateServiceCategoryCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<ServiceCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteServiceCategoryCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
