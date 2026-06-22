using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.History;

public sealed record GetPricingHistoryQuery(int Page, int PageSize) : IQuery<PaginatedList<PricingHistoryEntryDto>>;

public sealed class GetPricingHistoryHandler : IQueryHandler<GetPricingHistoryQuery, PaginatedList<PricingHistoryEntryDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetPricingHistoryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PricingHistoryEntryDto>> HandleAsync(GetPricingHistoryQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<PricingHistoryEntryDto>.CreateAsync(
            _db.PricingChangeLogs.AsNoTracking()
                .Where(x => x.BrandId == brandId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new PricingHistoryEntryDto(
                    x.Id, x.TargetKind, x.TargetId, x.Summary, x.ActorName, x.CreatedAt, x.RevertedAt)),
            q.Page, q.PageSize, ct);
    }
}
