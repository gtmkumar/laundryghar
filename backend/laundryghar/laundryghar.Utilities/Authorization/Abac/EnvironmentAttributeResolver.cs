using System.Security.Claims;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Supplies the environment slice (A2.3) — WHEN and FROM WHERE the request arrived.
///
/// <para><b>Why the channel is derived from <c>token_use</c> and not from a header.</b> A header
/// such as <c>X-Client</c> is set by the caller, so a policy like "refunds may not be issued from
/// the customer app" would be defeated by deleting the header. <c>token_use</c> is inside the signed
/// JWT and cannot be altered without the signing key, so it is the only channel evidence worth
/// making a decision on. The cost is that admin and POS both present as <c>token_use=user</c> and
/// so share one channel value; separating them needs a signed claim, not a header.</para>
/// </summary>
public sealed class EnvironmentAttributeResolver : IAttributeResolver
{
    public const string ResolverKey = "environment";

    public static class Channel
    {
        public const string Admin       = "admin";
        public const string CustomerApp = "customer_app";
        public const string RiderApp    = "rider_app";
        public const string Api         = "api";
        public const string Unknown     = "unknown";
    }

    private readonly IHttpContextAccessor _accessor;
    private readonly TimeProvider _clock;

    public EnvironmentAttributeResolver(IHttpContextAccessor accessor, TimeProvider? clock = null)
    {
        _accessor = accessor;
        _clock = clock ?? TimeProvider.System;
    }

    public string Key => ResolverKey;

    public ValueTask ResolveAsync(AbacAttributeBag bag, CancellationToken ct)
    {
        bag.Set(AbacAttributeKeys.EnvNow, _clock.GetUtcNow());

        var http = _accessor.HttpContext;

        // A null RemoteIpAddress is a genuinely unknown origin (in-process call, unusual transport).
        // Setting it to a known null lets `is_null` test for that case; a policy comparing an IP
        // range against null is false, not accidentally true.
        bag.Set(AbacAttributeKeys.EnvIp, http?.Connection.RemoteIpAddress?.ToString());

        bag.Set(AbacAttributeKeys.EnvChannel, ChannelFor(http?.User));

        return ValueTask.CompletedTask;
    }

    private static string ChannelFor(ClaimsPrincipal? principal)
    {
        var tokenUse = principal?.FindFirstValue("token_use");
        if (tokenUse is null) return Channel.Unknown;

        if (tokenUse == CustomerTokenClaims.TokenUseValue
         || tokenUse == CustomerTokenClaims.OAuthTokenUseValue) return Channel.CustomerApp;
        if (tokenUse == PartnerTokenClaims.TokenUseValue) return Channel.Api;
        if (tokenUse == TokenClaims.TokenUseValue)
        {
            // Riders ride the staff lane (token_use=user, user_type=rider) — see the rider
            // self-service policy — so the channel has to come off user_type for them.
            var userType = principal?.FindFirstValue("user_type");
            return userType == SharedDataModel.Enums.UserType.Rider ? Channel.RiderApp : Channel.Admin;
        }

        return Channel.Api; // api_key and anything else machine-issued
    }
}
