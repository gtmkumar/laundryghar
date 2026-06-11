namespace laundryghar.Mcp.Infrastructure.Auth;

/// <summary>
/// Authorization requirement: the bearer token must carry token_use=customer.
/// Prevents system-user or rider tokens from accessing customer MCP tools.
/// Mirrors the same requirement used by Catalog / Orders customer endpoints.
/// </summary>
public sealed class CustomerOnlyRequirement : IAuthorizationRequirement { }

/// <summary>Evaluates CustomerOnlyRequirement — succeeds only for customer JWTs.</summary>
public sealed class CustomerOnlyHandler : AuthorizationHandler<CustomerOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (string.Equals(tokenUse, "customer", StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
