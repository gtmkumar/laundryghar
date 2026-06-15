using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Queries.GetCustomerMe;

public sealed class GetCustomerMeHandler : IQueryHandler<GetCustomerMeQuery, CustomerMeResponse?>
{
    private readonly ICoreDbContext _db;
    public GetCustomerMeHandler(ICoreDbContext db) => _db = db;

    public async Task<CustomerMeResponse?> HandleAsync(GetCustomerMeQuery q, CancellationToken ct) =>
        await _db.Customers.AsNoTracking()
            .Where(x => x.Id == q.CustomerId)
            .Select(x => new CustomerMeResponse(
                x.Id, x.BrandId, x.PhoneE164,
                x.FirstName, x.LastName, x.DisplayName, x.Status))
            .FirstOrDefaultAsync(ct);
}
