namespace laundryghar.Catalog.Infrastructure.Auth;

/// <summary>
/// Authorization requirement: the bearer token must have token_use=customer.
/// Used by the "CustomerOnly" policy to prevent system-user tokens from accessing
/// customer-facing endpoints.
/// </summary>
public sealed class CustomerOnlyRequirement : IAuthorizationRequirement { }

/// <summary>Handles CustomerOnlyRequirement — succeeds only for customer JWTs.</summary>
public sealed class CustomerOnlyHandler : AuthorizationHandler<CustomerOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (string.Equals(tokenUse, CustomerTokenClaims.TokenUseValue, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
