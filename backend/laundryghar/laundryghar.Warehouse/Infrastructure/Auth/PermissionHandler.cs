namespace laundryghar.Warehouse.Infrastructure.Auth;

/// <summary>
/// Evaluates PermissionRequirement. Gate 1: token_use must be "user" —
/// customer tokens never satisfy admin permission policies (they are silently rejected).
/// Gate 2: platform_admin bypasses individual permission checks.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask; // customer tokens silently fail → 403

        var userType = context.User.FindFirstValue("user_type");
        if (userType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var perms = context.User.FindFirstValue("permissions");
        if (!string.IsNullOrEmpty(perms)
            && perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(requirement.PermissionCode, StringComparer.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
