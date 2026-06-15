using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Logistics.Riders.Commands.PayoutAdmin;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider payout (withdrawal) requests: review queue, approve / reject, and mark-paid
/// (which posts a cash_out cash-book entry). Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class RiderPayoutRequestsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/rider-payout-requests";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Payouts").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:rider.read");
        group.MapPost(Approve, "/{id:guid}/approve").RequireAuthorization("permission:rider.settle");
        group.MapPost(Reject, "/{id:guid}/reject").RequireAuthorization("permission:rider.settle");
        group.MapPost(MarkPaid, "/{id:guid}/mark-paid").RequireAuthorization("permission:rider.settle");
    }

    public static async Task<IResult> GetAll(string? status, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPayoutRequestsQuery(status), ct);
        return Results.Ok(new ListResponse<PayoutRequestAdminDto> { Status = true, Data = r.ToList() });
    }

    public static async Task<IResult> Approve(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewPayoutRequestCommand(id, true, null, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PayoutRequestAdminDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Reject(Guid id, RejectRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewPayoutRequestCommand(id, false, req.Reason, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PayoutRequestAdminDto> { Status = true, Data = r });
    }

    public static async Task<IResult> MarkPaid(Guid id, MarkPayoutPaidRequest? req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new MarkPayoutPaidCommand(id, req?.Reference, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PayoutRequestAdminDto> { Status = true, Data = r });
    }
}
