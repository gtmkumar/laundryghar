using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Packages;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — packages. All routes gated by permission:packages.manage.</summary>
public class PackagesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/packages";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Packages");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:packages.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:packages.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreatePackageRequest>>()
            .RequireAuthorization("permission:packages.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:packages.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:packages.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetPackagesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPackageByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreatePackageRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePackageCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/packages/{r.Id}", new SingleResponse<PackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdatePackageRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePackageCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PackageDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeletePackageCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
