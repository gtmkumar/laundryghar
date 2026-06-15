using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Commands.CreateGarment;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Queries.GetGarmentById;

public sealed record GetGarmentByIdQuery(Guid Id) : IQuery<GarmentDto?>;

public class GetGarmentByIdQueryHandler : IQueryHandler<GetGarmentByIdQuery, GarmentDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetGarmentByIdQueryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentDto?> HandleAsync(GetGarmentByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var g = await _db.Garments
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return g is null ? null : CreateGarmentCommandHandler.ToDto(g);
    }
}
