using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Platforms.Queries.GetPlatforms;

public sealed record GetPlatformsQuery(int Page = 1, int PageSize = 20) : IQuery<PaginatedList<PlatformDto>>;

public class GetPlatformsQueryHandler : IQueryHandler<GetPlatformsQuery, PaginatedList<PlatformDto>>
{
    private readonly ICoreDbContext _db;

    public GetPlatformsQueryHandler(ICoreDbContext db) => _db = db;

    public Task<PaginatedList<PlatformDto>> HandleAsync(GetPlatformsQuery query, CancellationToken cancellationToken) =>
        PaginatedList<PlatformDto>.CreateAsync(
            _db.Platforms.AsNoTracking().OrderBy(p => p.Name)
               .Select(p => new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt)),
            query.Page, query.PageSize, cancellationToken);
}
