using laundryghar.Identity.Infrastructure.Auth;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>Revokes all tokens in the refresh token's family (full session logout).</summary>
public sealed class LogoutHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly LaundryGharDbContext _db;
    private readonly IJwtTokenService     _jwt;

    public LogoutHandler(LaundryGharDbContext db, IJwtTokenService jwt)
    {
        _db  = db;
        _jwt = jwt;
    }

    public async Task<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);

        var token = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (token is null) return Unit.Value; // idempotent

        // Revoke entire family
        var family = await _db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in family)
        {
            t.RevokedAt     = DateTimeOffset.UtcNow;
            t.RevokedReason = "logout";
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
