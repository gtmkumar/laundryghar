namespace laundryghar.Logistics.Infrastructure.Auth;

/// <summary>
/// Authorization requirement: the bearer token must have token_use=user AND user_type=rider.
/// Used by the "RiderOnly" policy to gate the rider self-service lane.
/// Admin (non-rider) system tokens and customer tokens both fail this requirement.
/// </summary>
public sealed class RiderOnlyRequirement : IAuthorizationRequirement { }

/// <summary>Handles RiderOnlyRequirement — succeeds only for rider system JWTs.</summary>
public sealed class RiderOnlyHandler : AuthorizationHandler<RiderOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RiderOnlyRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        var userType = context.User.FindFirstValue("user_type");

        if (string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal)
            && string.Equals(userType, laundryghar.SharedDataModel.Enums.UserType.Rider, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
