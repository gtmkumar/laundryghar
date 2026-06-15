using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Coupons;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — coupons. All routes gated by permission:coupons.manage.</summary>
public class CouponsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/coupons";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Coupons");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:coupons.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:coupons.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateCouponRequest>>()
            .RequireAuthorization("permission:coupons.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:coupons.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:coupons.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetCouponsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<CouponDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetCouponByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CouponDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateCouponRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateCouponCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/coupons/{r.Id}", new SingleResponse<CouponDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateCouponRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateCouponCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<CouponDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteCouponCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
