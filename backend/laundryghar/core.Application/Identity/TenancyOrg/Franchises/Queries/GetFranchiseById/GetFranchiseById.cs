using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Franchises.Queries.GetFranchiseById;

public sealed record GetFranchiseByIdQuery(Guid Id) : IQuery<FranchiseDto?>;

public class GetFranchiseByIdQueryHandler : IQueryHandler<GetFranchiseByIdQuery, FranchiseDto?>
{
    private readonly ICoreDbContext _db;

    public GetFranchiseByIdQueryHandler(ICoreDbContext db) => _db = db;

    public Task<FranchiseDto?> HandleAsync(GetFranchiseByIdQuery query, CancellationToken cancellationToken) =>
        _db.Franchises.AsNoTracking().Where(f => f.Id == query.Id)
            .Select(f => new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
