using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.Incentives.Commands.CreateIncentiveRule;
using operations.Application.Logistics.Incentives.Commands.DeleteIncentiveRule;
using operations.Application.Logistics.Incentives.Commands.UpdateIncentiveRule;
using operations.Application.Logistics.Incentives.Dtos;
using operations.Application.Logistics.Incentives.Queries.GetIncentiveRules;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider incentive rules: brand-scoped bonus rules (trips_target|surge_bonus).
/// Bare-array list, create, update, soft-delete. Thin dispatch through <see cref="IDispatcher"/>.
/// No incentive-specific permission exists in the catalog, so reads reuse
/// <c>rider.read</c> and writes reuse <c>rider.manage</c> (closest rider manage perm).
/// </summary>
public class IncentiveRulesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/incentive-rules";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Incentives").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:rider.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateIncentiveRuleRequest>>()
            .RequireAuthorization("permission:rider.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdateIncentiveRuleRequest>>()
            .RequireAuthorization("permission:rider.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:rider.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, bool activeOnly = false)
    {
        var r = await dispatcher.QueryAsync(new GetIncentiveRulesQuery(activeOnly), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<IncentiveRuleDto>> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateIncentiveRuleRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateIncentiveRuleCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/incentive-rules/{r.Id}",
            new SingleResponse<IncentiveRuleDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateIncentiveRuleRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateIncentiveRuleCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<IncentiveRuleDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteIncentiveRuleCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
