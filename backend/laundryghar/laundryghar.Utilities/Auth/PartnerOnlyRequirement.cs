using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace laundryghar.Utilities.Auth;

/// <summary>
/// Authorization requirement: the bearer token must have token_use=partner.
/// Used by the "PartnerOnly" policy to gate the RaaS partner lane.
/// Customer tokens and staff/system-user tokens both fail this requirement.
/// </summary>
public sealed class PartnerOnlyRequirement : IAuthorizationRequirement { }

/// <summary>Handles PartnerOnlyRequirement — succeeds only for partner JWTs.</summary>
public sealed class PartnerOnlyHandler : AuthorizationHandler<PartnerOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PartnerOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (string.Equals(tokenUse, PartnerTokenClaims.TokenUseValue, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Authorization requirement: the bearer token must have token_use=partner AND partner_role=partner_admin.
/// Used by the "PartnerAdmin" policy to gate partner-admin-only actions within the RaaS partner lane.
/// Partner-operator tokens and non-partner tokens both fail this requirement.
/// </summary>
public sealed class PartnerAdminRequirement : IAuthorizationRequirement { }

/// <summary>Handles PartnerAdminRequirement — succeeds only for partner-admin JWTs.</summary>
public sealed class PartnerAdminHandler : AuthorizationHandler<PartnerAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PartnerAdminRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        var partnerRole = context.User.FindFirstValue(PartnerTokenClaims.PartnerRoleClaim);

        if (string.Equals(tokenUse, PartnerTokenClaims.TokenUseValue, StringComparison.Ordinal)
            && string.Equals(partnerRole, PartnerRole.Admin, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
