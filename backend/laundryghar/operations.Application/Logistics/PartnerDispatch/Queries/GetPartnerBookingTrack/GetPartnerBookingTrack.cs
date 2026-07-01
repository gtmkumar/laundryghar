using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerDispatch.Common;
using operations.Application.Logistics.PartnerDispatch.Dtos;

namespace operations.Application.Logistics.PartnerDispatch.Queries.GetPartnerBookingTrack;

/// <summary>Partner-facing: track the dispatch for a booking the caller owns.</summary>
/// <param name="PartnerBookingId">The booking to track.</param>
public sealed record GetPartnerBookingTrackQuery(Guid PartnerBookingId) : IQuery<PartnerBookingTrackDto>;

/// <summary>
/// Returns the dispatch status + rider + last-known location + verification/proof state for one of
/// the calling partner's bookings. No explicit partner_id filter is needed: the
/// <c>rls_partner_or_brand</c> policy auto-scopes partner_dispatches to app.current_partner_id (the
/// partner arm), so a partner session only ever sees its OWN dispatch rows. A booking with no
/// dispatch yet — or a booking id the caller does not own — yields a "not_dispatched" track (no
/// leak: an un-owned dispatch is simply invisible to this session).
/// </summary>
public sealed class GetPartnerBookingTrackHandler
    : IQueryHandler<GetPartnerBookingTrackQuery, PartnerBookingTrackDto>
{
    private readonly IOperationsDbContext _db;

    public GetPartnerBookingTrackHandler(IOperationsDbContext db) => _db = db;

    public async Task<PartnerBookingTrackDto> HandleAsync(
        GetPartnerBookingTrackQuery query, CancellationToken ct)
    {
        var dispatch = await _db.PartnerDispatches
            .AsNoTracking()
            .Where(d => d.PartnerBookingId == query.PartnerBookingId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return PartnerDispatchMapper.ToTrackDto(query.PartnerBookingId, dispatch);
    }
}
