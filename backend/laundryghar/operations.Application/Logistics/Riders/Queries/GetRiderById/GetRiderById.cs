using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Queries.GetRiderById;

// ── Get Rider By Id ───────────────────────────────────────────────────────────

public sealed record GetRiderByIdQuery(Guid Id) : IQuery<RiderDto?>;

public sealed class GetRiderByIdHandler : IQueryHandler<GetRiderByIdQuery, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderDto?> HandleAsync(GetRiderByIdQuery query, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // read riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var dto = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dto, _user);
    }
}
