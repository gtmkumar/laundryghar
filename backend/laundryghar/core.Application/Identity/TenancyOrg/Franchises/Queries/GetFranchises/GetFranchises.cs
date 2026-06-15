using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Franchises.Queries.GetFranchises;

public sealed record GetFranchisesQuery(Guid? BrandId, int Page = 1, int PageSize = 20) : IQuery<PaginatedList<FranchiseDto>>;

public class GetFranchisesQueryHandler : IQueryHandler<GetFranchisesQuery, PaginatedList<FranchiseDto>>
{
    private readonly ICoreDbContext _db;

    public GetFranchisesQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<FranchiseDto>> HandleAsync(GetFranchisesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Franchises.AsNoTracking().AsQueryable();
        if (query.BrandId.HasValue) q = q.Where(f => f.BrandId == query.BrandId.Value);
        return PaginatedList<FranchiseDto>.CreateAsync(
            q.OrderBy(f => f.LegalName).Select(f => new FranchiseDto(f.Id, f.BrandId, f.Code, f.LegalName, f.OnboardingStatus, f.Status, f.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
    }
}
