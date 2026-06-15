using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Promotions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — promotions. All routes gated by permission:promotions.manage.</summary>
public class PromotionsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/promotions";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Promotions");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:promotions.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:promotions.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreatePromotionRequest>>()
            .RequireAuthorization("permission:promotions.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:promotions.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:promotions.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetPromotionsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PromotionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPromotionByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PromotionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreatePromotionRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePromotionCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/promotions/{r.Id}", new SingleResponse<PromotionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdatePromotionRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePromotionCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PromotionDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeletePromotionCommand(id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
