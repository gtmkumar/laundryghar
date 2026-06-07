using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.SharedDataModel.Enums;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.Auth.Commands;

// ── DTOs ────────────────────────────────────────────────────────────────────
public sealed record InvitePreviewDto(bool Valid, string? Email, string? Name);
public sealed record AcceptInviteRequest(string Token, string NewPassword);

// ── Preview an invitation token (public, unauthenticated) ───────────────────
public sealed record GetInvitePreviewQuery(string Token) : IRequest<InvitePreviewDto>;

public sealed class GetInvitePreviewHandler : IRequestHandler<GetInvitePreviewQuery, InvitePreviewDto>
{
    private readonly LaundryGharDbContext _db;
    public GetInvitePreviewHandler(LaundryGharDbContext db) => _db = db;

    public async Task<InvitePreviewDto> Handle(GetInvitePreviewQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Token)) return new InvitePreviewDto(false, null, null);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.InvitationToken == q.Token && u.Status == UserStatus.Invited)
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null) return new InvitePreviewDto(false, null, null);

        var name = await _db.UserProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);

        return new InvitePreviewDto(true, user.Email, string.IsNullOrWhiteSpace(name) ? null : name);
    }
}

// ── Accept an invitation: set password, activate (public, unauthenticated) ──
public sealed record AcceptInviteCommand(AcceptInviteRequest Request) : IRequest<bool>;

public sealed class AcceptInviteHandler : IRequestHandler<AcceptInviteCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPasswordHasher _hasher;
    public AcceptInviteHandler(LaundryGharDbContext db, IPasswordHasher hasher) { _db = db; _hasher = hasher; }

    public async Task<bool> Handle(AcceptInviteCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.NewPassword) || r.NewPassword.Length < 8)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["newPassword"] = ["Password must be at least 8 characters."] });

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.InvitationToken == r.Token && u.Status == UserStatus.Invited, ct);
        if (user is null)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["token"] = ["This invitation is invalid or has already been used."] });

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash        = _hasher.Hash(r.NewPassword);
        user.PasswordChangedAt   = now;
        user.MustChangePassword  = false;
        user.Status              = UserStatus.Active;
        user.InvitationToken     = null;
        user.InvitationAcceptedAt = now;
        user.UpdatedAt           = now;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
