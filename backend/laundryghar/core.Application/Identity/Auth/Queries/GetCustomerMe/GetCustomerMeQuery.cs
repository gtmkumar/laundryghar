using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Queries.GetCustomerMe;

/// <summary>Returns the authenticated customer's profile, or null if no matching customer row.</summary>
public sealed record GetCustomerMeQuery(Guid CustomerId) : IQuery<CustomerMeResponse?>;
