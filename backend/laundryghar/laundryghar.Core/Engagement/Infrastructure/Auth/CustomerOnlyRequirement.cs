namespace laundryghar.Engagement.Infrastructure.Auth;

/// <summary>Authorization requirement: bearer token must have token_use=customer.</summary>
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
