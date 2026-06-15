using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace core.WebApi.Mcp.Infrastructure.Auth;

/// <summary>Evaluates <see cref="McpCustomerOnlyRequirement"/>.</summary>
public sealed class McpCustomerOnlyHandler : AuthorizationHandler<McpCustomerOnlyRequirement>
{
    private const string OAuthTokenUse   = "customer_mcp";
    private const string DirectTokenUse  = "customer";
    private const string RequiredScope   = "mcp:booking";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        McpCustomerOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");

        if (string.Equals(tokenUse, OAuthTokenUse, StringComparison.Ordinal))
        {
            // OAuth 2.1 path: must also carry the mcp:booking scope claim.
            // The scope claim is a space-separated list per RFC 6749 §3.3.
            var scope = context.User.FindFirstValue("scope") ?? string.Empty;
            if (scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Contains(RequiredScope, StringComparer.Ordinal))
            {
                context.Succeed(requirement);
            }
            // else: token_use=customer_mcp without mcp:booking — fail implicitly (do not call Succeed)
        }
        else if (string.Equals(tokenUse, DirectTokenUse, StringComparison.Ordinal))
        {
            // Direct customer app token (mobile app, POS) — no scope required.
            context.Succeed(requirement);
        }
        // All other token_use values (user, rider, etc.) are rejected implicitly.

        return Task.CompletedTask;
    }
}
