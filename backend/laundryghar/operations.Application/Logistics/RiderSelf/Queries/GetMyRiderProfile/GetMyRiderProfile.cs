using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.RiderSelf.Queries.GetMyRiderProfile;

// ── Get own rider profile ─────────────────────────────────────────────────────

public sealed record GetMyRiderProfileQuery(Guid UserId) : IQuery<RiderDto?>;

public sealed class GetMyRiderProfileHandler : IQueryHandler<GetMyRiderProfileQuery, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    public GetMyRiderProfileHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderDto?> HandleAsync(GetMyRiderProfileQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var r = await _db.Riders.FirstOrDefaultAsync(x => x.UserId == query.UserId, ct);
        if (r is null) return null;
        return await CreateRiderHandler.LoadEnrichedAsync(_db, r, ct);
    }
}
