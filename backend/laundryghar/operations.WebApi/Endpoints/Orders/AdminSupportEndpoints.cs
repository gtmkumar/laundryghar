using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Orders.Support;

namespace operations.WebApi.Endpoints.Orders;

/// <summary>
/// Admin-facing support ticket endpoints (inbox, detail, agent reply).
/// Brand scope comes from <see cref="ICurrentUser.RequireBrandId"/> (platform admins pass X-Brand-Id);
/// ticket/message row isolation is additionally enforced by RLS on <c>IOperationsDbContext</c>.
/// Reads are gated by <c>support.read</c>; mutations by <c>support.manage</c>.
/// </summary>
public class AdminSupportEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/support";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Support");

        group.MapGet(GetTickets, "/tickets").RequireAuthorization("permission:support.read");
        group.MapGet(GetTicketDetail, "/tickets/{id:guid}").RequireAuthorization("permission:support.read");
        group.MapPost(PostMessage, "/tickets/{id:guid}/messages").RequireAuthorization("permission:support.manage");
    }

    /// <summary>Inbox: brand-scoped tickets, optionally filtered by <paramref name="status"/> (?status=open).</summary>
    public static async Task<IResult> GetTickets(
        ICurrentUser u, IDispatcher dispatcher, CancellationToken ct, string? status = null)
    {
        var brandId = u.RequireBrandId();
        var r = await dispatcher.QueryAsync(new GetTicketsInboxQuery(brandId, status), ct);
        return Results.Ok(new ListResponse<SupportTicketDto> { Status = true, Data = r.ToList() });
    }

    /// <summary>Full ticket with its message thread. IsAdmin=true bypasses the requester IDOR guard;
    /// brand isolation is handled by RLS on the DbContext.</summary>
    public static async Task<IResult> GetTicketDetail(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetTicketDetailQuery(id, null, true), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<SupportTicketDetailDto> { Status = true, Data = r });
    }

    /// <summary>Agent reply. Sender is the acting admin; an agent reply moves an open ticket to in_progress.</summary>
    public static async Task<IResult> PostMessage(Guid id, PostMessageRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new PostTicketMessageCommand(id, "agent", u.UserId, req.Body, true, null), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<TicketMessageDto> { Status = true, Data = r });
    }
}
