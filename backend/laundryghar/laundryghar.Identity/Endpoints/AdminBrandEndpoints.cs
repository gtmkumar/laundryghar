using laundryghar.Identity.Application.TenancyOrg.Commands;
using laundryghar.Identity.Application.TenancyOrg.Dtos;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>GET/POST/PUT/DELETE /api/v1/admin/brands</summary>
public static class AdminBrandEndpoints
{
    public static RouteGroupBuilder MapBrandEndpoints(this RouteGroupBuilder group)
    {
        var brands = group.MapGroup("/brands").WithTags("Admin - Brands").RequireAuthorization();

        brands.MapGet("/", async (
            [AsParameters] BrandListParams p,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBrandsQuery(p), ct);
            return Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.PaginatedListResponse<BrandDto>
                { Status = true, Data = result });
        })
        .WithName("GetBrands")
        .RequireAuthorization("permission:brands.list");

        brands.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBrandByIdQuery(id), ct);
            return result is null
                ? Results.NotFound(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = false, Message = new Message { ResponseMessage = "Brand not found." } })
                : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<BrandDto> { Status = true, Data = result });
        })
        .WithName("GetBrandById")
        .RequireAuthorization("permission:brands.read");

        brands.MapPost("/", async (
            CreateBrandRequest req,
            ICurrentUser currentUser,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateBrandCommand(req, currentUser.UserId), ct);
            return Results.Created($"/api/v1/admin/brands/{result.Id}",
                new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<BrandDto> { Status = true, Data = result });
        })
        .WithName("CreateBrand")
        .RequireAuthorization("permission:brands.create");

        brands.MapPut("/{id:guid}", async (
            Guid id,
            UpdateBrandRequest req,
            ICurrentUser currentUser,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateBrandCommand(id, req, currentUser.UserId), ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.SingleResponse<BrandDto> { Status = true, Data = result });
        })
        .WithName("UpdateBrand")
        .RequireAuthorization("permission:brands.update");

        brands.MapDelete("/{id:guid}", async (
            Guid id,
            ICurrentUser currentUser,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteBrandCommand(id, currentUser.UserId), ct);
            return result
                ? Results.Ok(new laundryghar.Utilities.ApiResponse.ResponseUtil.Response { Status = true, Message = new Message { ResponseMessage = "Brand deleted." } })
                : Results.NotFound();
        })
        .WithName("DeleteBrand")
        .RequireAuthorization("permission:brands.delete");

        return group;
    }
}
