using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Customer.Admin.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Customer.Admin.Queries;

public sealed record GetCustomersQuery(int Page, int PageSize, string? Status, string? Search) : IRequest<PaginatedList<AdminCustomerDto>>;

public sealed class GetCustomersHandler : IRequestHandler<GetCustomersQuery, PaginatedList<AdminCustomerDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCustomersHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<AdminCustomerDto>> Handle(GetCustomersQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate enforced in-handler (defense-in-depth; RLS also active for non-superuser roles).
        var query = _db.Customers.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(x => x.Status == q.Status);
        if (!string.IsNullOrEmpty(q.Search))
        {
            var s = q.Search.ToLower();
            query = query.Where(x =>
                (x.PhoneE164 != null && x.PhoneE164.Contains(s)) ||
                (x.Email     != null && x.Email.ToLower().Contains(s)) ||
                (x.FirstName != null && x.FirstName.ToLower().Contains(s)) ||
                (x.LastName  != null && x.LastName.ToLower().Contains(s)));
        }

        return PaginatedList<AdminCustomerDto>.CreateAsync(
            query.OrderByDescending(x => x.CreatedAt).Select(x => ToDto(x)),
            q.Page, q.PageSize, ct);
    }

    internal static AdminCustomerDto ToDto(SharedDataModel.Entities.CustomerCatalog.Customer c) => new(
        c.Id, c.BrandId, c.CustomerCode, c.PhoneE164, c.Email, c.FirstName, c.LastName, c.DisplayName,
        c.Gender, c.DateOfBirth, c.Locale, c.Timezone, c.LifetimeOrders, c.LifetimeSpend,
        c.LoyaltyPointsBalance, c.WalletBalance, c.CustomerSegment, c.RiskFlag, c.Status,
        c.CreatedAt, c.UpdatedAt);
}

public sealed record GetCustomerByIdQuery(Guid Id) : IRequest<AdminCustomerDto?>;

public sealed class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, AdminCustomerDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCustomerByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto?> Handle(GetCustomerByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetCustomersHandler.ToDto(e);
    }
}
