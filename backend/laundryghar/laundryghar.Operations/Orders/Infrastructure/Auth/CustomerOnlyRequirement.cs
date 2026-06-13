namespace laundryghar.Orders.Infrastructure.Auth;

public sealed class CustomerOnlyRequirement : IAuthorizationRequirement { }

public sealed class CustomerOnlyHandler : AuthorizationHandler<CustomerOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CustomerOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (string.Equals(tokenUse, CustomerTokenClaims.TokenUseValue, StringComparison.Ordinal))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
