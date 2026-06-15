using core.Application.Identity.TenancyOrg.Dtos;
using core.Application.Identity.TenancyOrg.Brands.Commands.CreateBrand;
using core.Application.Identity.TenancyOrg.Brands.Commands.DeleteBrand;
using core.Application.Identity.TenancyOrg.Brands.Commands.UpdateBrand;
using core.Application.Identity.TenancyOrg.Brands.Queries.GetBrandById;
using core.Application.Identity.TenancyOrg.Brands.Queries.GetBrands;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — platform-level Brand CRUD. Brands are NOT brand-scoped. Thin: each method dispatches a
/// command/query through <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class AdminBrands : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/brands";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Brands").RequireAuthorization();

        group.MapGet(GetAll).RequireAuthorization("permission:brands.list");
        group.MapGet(GetById, "{id:guid}").RequireAuthorization("permission:brands.read");
        group.MapPost(Create).RequireAuthorization("permission:brands.create");
        group.MapPut(Update, "{id:guid}").RequireAuthorization("permission:brands.update");
        group.MapDelete(Delete, "{id:guid}").RequireAuthorization("permission:brands.delete");
    }

    public static async Task<IResult> GetAll([AsParameters] BrandListParams p, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBrandsQuery(p), ct);
        return Results.Ok(new PaginatedListResponse<BrandDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBrandByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<BrandDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateBrandRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateBrandCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/brands/{data.Id}",
            new SingleResponse<BrandDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateBrandRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateBrandCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<BrandDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteBrandCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
