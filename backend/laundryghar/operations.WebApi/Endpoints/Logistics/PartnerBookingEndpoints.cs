using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.PartnerBookings.Commands.CreatePartnerBooking;
using operations.Application.Logistics.PartnerBookings.Dtos;
using operations.Application.Logistics.PartnerBookings.Queries.GetMyPartnerBookings;
using operations.Application.Logistics.PartnerDispatch.Dtos;
using operations.Application.Logistics.PartnerDispatch.Queries.GetPartnerBookingTrack;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// RaaS partner booking lane (/api/v1/partner/*, issue #14 MVP-6). Group-gated by the "PartnerOnly"
/// policy (token_use=partner). All isolation is by partner_id: the RLS interceptor sets
/// app.current_partner_id from the token, so rls_partner scopes both the create WITH CHECK and the
/// list to the caller's own partner. The create actor (created_by_partner_user_id) is the JWT sub.
///
/// POST /api/v1/partner/bookings              (PartnerOnly)
/// GET  /api/v1/partner/bookings              (PartnerOnly)
/// GET  /api/v1/partner/bookings/{id}/track   (PartnerOnly) — FULL-11b dispatch track
/// </summary>
public class PartnerBookingEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/partner";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Partner - Bookings").RequireAuthorization("PartnerOnly");

        group.MapPost(Create, "/bookings")
            .AddEndpointFilter<ValidationFilter<CreatePartnerBookingRequest>>();
        group.MapGet(List, "/bookings");
        // Dispatch track (FULL-11b). No permission code: partner authz in the MVP is the
        // partner_role JWT claim + partner_id RLS (docs/rbac.md §4/§10), matching the other
        // partner endpoints — the rls_partner_or_brand partner arm scopes the read to the caller.
        group.MapGet(Track, "/bookings/{id:guid}/track");
    }

    public static async Task<IResult> Create(
        CreatePartnerBookingRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        // sub = partner_user_id for a partner token; PartnerOnly guarantees it is present.
        if (u.UserId is not { } partnerUserId || partnerUserId == Guid.Empty)
            return Results.Unauthorized();

        var r = await dispatcher.SendAsync(new CreatePartnerBookingCommand(req, partnerUserId), ct);
        return Results.Created($"/api/v1/partner/bookings/{r.Id}",
            new SingleResponse<PartnerBookingDto> { Status = true, Data = r });
    }

    public static async Task<IResult> List(IDispatcher dispatcher, CancellationToken ct)
    {
        var list = await dispatcher.QueryAsync(new GetMyPartnerBookingsQuery(), ct);
        return Results.Ok(new ListResponse<PartnerBookingDto> { Status = true, Data = list });
    }

    public static async Task<IResult> Track(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var track = await dispatcher.QueryAsync(new GetPartnerBookingTrackQuery(id), ct);
        return Results.Ok(new SingleResponse<PartnerBookingTrackDto> { Status = true, Data = track });
    }
}
