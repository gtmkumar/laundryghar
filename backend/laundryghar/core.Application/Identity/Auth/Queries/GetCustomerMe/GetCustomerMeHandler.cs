using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Queries.GetCustomerMe;

/// <summary>
/// Returns the authenticated customer's profile plus everything the app needs on launch:
/// which sign-in methods are linked, whether a PIN unlock is available, and which profile
/// fields are still blank (for the dashboard completion ribbon).
/// </summary>
public sealed class GetCustomerMeHandler : IQueryHandler<GetCustomerMeQuery, CustomerMeResponse?>
{
    private readonly ICoreDbContext _db;
    public GetCustomerMeHandler(ICoreDbContext db) => _db = db;

    public async Task<CustomerMeResponse?> HandleAsync(GetCustomerMeQuery q, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == q.CustomerId, ct);

        if (customer is null) return null;

        var linkedProviders = await _db.CustomerIdentities.AsNoTracking()
            .Where(i => i.CustomerId == customer.Id)
            .Select(i => i.Provider)
            .ToListAsync(ct);

        return new CustomerMeResponse(
            CustomerId:        customer.Id,
            BrandId:           customer.BrandId,
            Phone:             customer.PhoneE164,
            FirstName:         customer.FirstName,
            LastName:          customer.LastName,
            DisplayName:       customer.DisplayName,
            Status:            customer.Status,
            Email:             customer.Email,
            AvatarUrl:         customer.AvatarUrl,
            PhoneVerified:     customer.PhoneVerifiedAt is not null,
            EmailVerified:     customer.EmailVerifiedAt is not null,
            HasPin:            !string.IsNullOrEmpty(customer.PinHash),
            LinkedProviders:   linkedProviders,
            ProfileCompletion: CustomerProfileCompletion.Evaluate(customer));
    }
}
