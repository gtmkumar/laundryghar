using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Commands.CustomerLogout;

/// <summary>Revokes all tokens in a customer refresh token family (full session logout).</summary>
public sealed class CustomerLogoutHandler : ICommandHandler<CustomerLogoutCommand, bool>
{
    private readonly ICoreDbContext   _db;
    private readonly IJwtTokenService _jwt;

    public CustomerLogoutHandler(ICoreDbContext db, IJwtTokenService jwt)
    {
        _db  = db;
        _jwt = jwt;
    }

    public async Task<bool> HandleAsync(CustomerLogoutCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);

        var token = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (token is null) return true; // idempotent

        var family = await _db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in family)
        {
            t.RevokedAt     = DateTimeOffset.UtcNow;
            t.RevokedReason = "logout";
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
