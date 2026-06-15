using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.LoyaltyPrograms;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — loyalty programs. All routes gated by permission:loyalty.manage.</summary>
public class LoyaltyProgramsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/loyalty-programs";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Loyalty Programs");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:loyalty.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:loyalty.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateLoyaltyProgramRequest>>()
            .RequireAuthorization("permission:loyalty.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:loyalty.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:loyalty.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetLoyaltyProgramsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<LoyaltyProgramDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetLoyaltyProgramByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateLoyaltyProgramRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateLoyaltyProgramCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/loyalty-programs/{r.Id}", new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateLoyaltyProgramRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateLoyaltyProgramCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<LoyaltyProgramDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteLoyaltyProgramCommand(id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
