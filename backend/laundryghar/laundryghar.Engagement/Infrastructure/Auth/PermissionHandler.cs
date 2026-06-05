namespace laundryghar.Engagement.Infrastructure.Auth;

/// <summary>
/// Evaluates PermissionRequirement by checking the "permissions" claim in the JWT.
/// Customer tokens (token_use=customer) never satisfy admin permission policies.
/// Platform admins (user_type=platform_admin) bypass individual permission checks.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask;

        var userType = context.User.FindFirstValue("user_type");
        if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var permsClaim = context.User.FindFirstValue("permissions");
        if (!string.IsNullOrEmpty(permsClaim))
        {
            var perms = permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (perms.Contains(requirement.PermissionCode, StringComparer.OrdinalIgnoreCase))
                context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
