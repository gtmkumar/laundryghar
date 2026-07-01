using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerBookings.Common;
using operations.Application.Logistics.PartnerBookings.Dtos;

namespace operations.Application.Logistics.PartnerBookings.Queries.GetMyPartnerBookings;

/// <summary>Lists the calling partner's bookings. No explicit partner_id filter is needed: the
/// rls_partner policy auto-scopes partner_bookings to app.current_partner_id (set from the token),
/// so this only ever returns the caller's own rows.</summary>
public sealed record GetMyPartnerBookingsQuery : IQuery<List<PartnerBookingDto>>;

public sealed class GetMyPartnerBookingsHandler : IQueryHandler<GetMyPartnerBookingsQuery, List<PartnerBookingDto>>
{
    private readonly IOperationsDbContext _db;

    public GetMyPartnerBookingsHandler(IOperationsDbContext db) => _db = db;

    public async Task<List<PartnerBookingDto>> HandleAsync(GetMyPartnerBookingsQuery query, CancellationToken ct)
    {
        var rows = await _db.PartnerBookings
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(PartnerBookingMapper.ToDto).ToList();
    }
}
