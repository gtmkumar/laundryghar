using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.CapacityConfigs.Commands.CreateCapacityConfig;
using operations.Application.Logistics.CapacityConfigs.Dtos;

namespace operations.Application.Logistics.CapacityConfigs.Queries.GetCapacityConfigById;

// ── Get CapacityConfig By Id ──────────────────────────────────────────────────

public sealed record GetCapacityConfigByIdQuery(Guid Id) : IQuery<RiderCapacityConfigDto?>;

public sealed class GetCapacityConfigByIdHandler
    : IQueryHandler<GetCapacityConfigByIdQuery, RiderCapacityConfigDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetCapacityConfigByIdHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderCapacityConfigDto?> HandleAsync(GetCapacityConfigByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var c = await _db.RiderCapacityConfigs
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return c is null ? null : CreateCapacityConfigHandler.ToDto(c);
    }
}
