using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Impersonation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Impersonation.Commands;

public sealed record StartImpersonationSessionCommand(Guid GrantId, Guid SupportUserId)
    : ICommand<ImpersonationSessionDto>;

/// <summary>
/// Mints the session token once a provider has approved (§7).
///
/// <para><b>The token keeps support's own identity.</b> <c>sub</c> stays the support engineer's user
/// id; what changes is <c>brand_id</c> (so RLS scopes them into the tenant, which they otherwise
/// cannot see at all) plus the two <c>imp_*</c> claims. A token that impersonated the owner's
/// identity would make every audit row name the wrong human — the exact failure an impersonation
/// audit exists to prevent.</para>
///
/// <para><b>The token's expiry is not the security boundary.</b> The access token carries the host's
/// ordinary lifetime, which may outlast the grant; <c>ExpiresIn</c> reports the GRANT's remaining
/// seconds so the client shows the truth. The boundary itself is
/// <c>ImpersonationGuardMiddleware</c>, which re-reads grant state on every request — so an expired
/// or revoked grant stops working immediately even while its token is still cryptographically
/// valid.</para>
/// </summary>
public sealed class StartImpersonationSessionCommandHandler
    : ICommandHandler<StartImpersonationSessionCommand, ImpersonationSessionDto>
{
    private readonly ICoreDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IImpersonationStateStore _state;
    private readonly IAuditWriter _audit;
    private readonly JwtSettings _jwtSettings;
    private readonly IConfiguration _config;

    public StartImpersonationSessionCommandHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IImpersonationStateStore state,
        IAuditWriter audit,
        IOptions<JwtSettings> jwtOptions,
        IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _state = state;
        _audit = audit;
        _jwtSettings = jwtOptions.Value;
        _config = config;
    }

    public async Task<ImpersonationSessionDto> HandleAsync(
        StartImpersonationSessionCommand cmd, CancellationToken ct)
    {
        var state = await _state.GetAsync(cmd.GrantId, ct)
            ?? throw new ForbiddenException("No such impersonation request.");

        if (state.Status != ImpersonationGrantStatus.Approved)
            throw new ForbiddenException(
                $"This request is {state.Status}. Only an approved request can start a session.");

        // The grant names one person. Anyone else presenting it — including another support
        // engineer on the same team — is not who the provider agreed to let in.
        if (state.SupportUserId != cmd.SupportUserId)
            throw new ForbiddenException("This approval was granted to a different person.");

        var user = await _db.Users.FirstOrDefaultAsync(
                       u => u.Id == cmd.SupportUserId && u.DeletedAt == null, ct)
                   ?? throw new ForbiddenException("User not found.");

        var baseClaims = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user,
            enforceEntitlement: _config.GetValue<bool>("Entitlement:Enforced"), ct: ct);

        var sessionClaims = baseClaims with
        {
            BrandId = state.BrandId,
            ImpersonationGrantId = cmd.GrantId,
            ImpersonationScope = state.Scope,
        };

        var token = _jwt.CreateAccessToken(sessionClaims);

        var remaining = state.ExpiresAt is { } exp
            ? (int)Math.Max(0, (exp - DateTimeOffset.UtcNow).TotalSeconds)
            : _jwtSettings.AccessMinutes * 60;

        await _audit.WriteAsync("impersonation.session_started", "impersonation_grant", cmd.GrantId,
            newValues: new { brandId = state.BrandId, supportUserId = cmd.SupportUserId, scope = state.Scope },
            ct: ct);

        return new ImpersonationSessionDto(token, remaining, cmd.GrantId, state.BrandId, state.Scope);
    }
}
