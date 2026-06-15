using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Platforms.Queries.GetPlatformById;

public sealed record GetPlatformByIdQuery(Guid Id) : IQuery<PlatformDto?>;

public class GetPlatformByIdQueryHandler : IQueryHandler<GetPlatformByIdQuery, PlatformDto?>
{
    private readonly ICoreDbContext _db;

    public GetPlatformByIdQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PlatformDto?> HandleAsync(GetPlatformByIdQuery query, CancellationToken cancellationToken) =>
        _db.Platforms.AsNoTracking()
            .Where(p => p.Id == query.Id)
            .Select(p => new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
