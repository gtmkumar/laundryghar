using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Analytics.Reporting.Queries;

public sealed record GetCustomerLtvQuery(int Page, int PageSize) : IQuery<PaginatedList<CustomerLtv>>;

public sealed class GetCustomerLtvHandler : IQueryHandler<GetCustomerLtvQuery, PaginatedList<CustomerLtv>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetCustomerLtvHandler(ICommerceDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public Task<PaginatedList<CustomerLtv>> HandleAsync(GetCustomerLtvQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.CustomerLtvs
            .AsNoTracking()
            .Where(x => x.BrandId == brandId)
            .OrderByDescending(x => x.LifetimeRevenue);

        return PaginatedList<CustomerLtv>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}
