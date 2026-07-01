using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.PartnerDispatch.Commands.AssignPartnerDispatch;
using operations.Application.Logistics.PartnerDispatch.Commands.UpdatePartnerDispatchStatus;
using operations.Application.Logistics.PartnerDispatch.Dtos;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin/Fleet — RaaS partner dispatch (FULL-11b / issue #14). Brand-staff assign a rider to a
/// partner booking and advance the dispatch. Gated by the existing high-risk logistics permission
/// <c>delivery.assign</c> ("Assign rider to delivery"). These run in a brand-staff session, so the
/// <c>rls_partner_or_brand</c> BRAND arm (brand_id = current_brand_id) grants visibility of the
/// dispatches the staff's own fleet serves — while the owning partner still tracks the same rows
/// via the partner arm (dual visibility).
///
/// POST /api/v1/admin/partner-dispatches               (permission:delivery.assign)
/// PATCH /api/v1/admin/partner-dispatches/{id}/status  (permission:delivery.assign)
/// </summary>
public class PartnerDispatchesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/partner-dispatches";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Partner Dispatches").RequireAuthorization();

        group.MapPost(Assign, "/")
            .AddEndpointFilter<ValidationFilter<AssignPartnerDispatchRequest>>()
            .RequireAuthorization("permission:delivery.assign");

        group.MapPatch(UpdateStatus, "/{id:guid}/status")
            .AddEndpointFilter<ValidationFilter<UpdatePartnerDispatchStatusRequest>>()
            .RequireAuthorization("permission:delivery.assign");
    }

    public static async Task<IResult> Assign(
        AssignPartnerDispatchRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AssignPartnerDispatchCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/partner-dispatches/{r.Id}",
            new SingleResponse<PartnerDispatchDto> { Status = true, Data = r });
    }

    public static async Task<IResult> UpdateStatus(
        Guid id, UpdatePartnerDispatchStatusRequest req, ICurrentUser u, IDispatcher dispatcher,
        CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePartnerDispatchStatusCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<PartnerDispatchDto> { Status = true, Data = r });
    }
}
