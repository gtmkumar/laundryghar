using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Register / upsert a rider Expo push token ─────────────────────────────────

public sealed record RegisterRiderPushTokenCommand(
    Guid   UserId,
    Guid   BrandId,
    string Token,
    string Platform) : IRequest;

public sealed class RegisterRiderPushTokenHandler
    : IRequestHandler<RegisterRiderPushTokenCommand>
{
    private readonly LaundryGharDbContext _db;

    public RegisterRiderPushTokenHandler(LaundryGharDbContext db) => _db = db;

    public async Task Handle(RegisterRiderPushTokenCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Upsert on the unique token column.
        // Re-registering an existing token re-points it to this rider and re-activates.
        var existing = await _db.PushTokens
            .FirstOrDefaultAsync(pt => pt.Token == cmd.Token, ct);

        if (existing is not null)
        {
            existing.BrandId    = cmd.BrandId;
            existing.UserType   = "rider";
            existing.UserId     = cmd.UserId;
            existing.CustomerId = null;
            existing.Platform   = cmd.Platform;
            existing.IsActive   = true;
            existing.UpdatedAt  = now;
        }
        else
        {
            _db.PushTokens.Add(new PushToken
            {
                Id         = Guid.NewGuid(),
                BrandId    = cmd.BrandId,
                UserType   = "rider",
                UserId     = cmd.UserId,
                CustomerId = null,
                Platform   = cmd.Platform,
                Token      = cmd.Token,
                IsActive   = true,
                CreatedAt  = now,
                UpdatedAt  = now,
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}

// ── Deactivate a rider push token (logout) ────────────────────────────────────

public sealed record DeactivateRiderPushTokenCommand(
    Guid   UserId,
    string Token) : IRequest;

public sealed class DeactivateRiderPushTokenHandler
    : IRequestHandler<DeactivateRiderPushTokenCommand>
{
    private readonly LaundryGharDbContext _db;

    public DeactivateRiderPushTokenHandler(LaundryGharDbContext db) => _db = db;

    public async Task Handle(DeactivateRiderPushTokenCommand cmd, CancellationToken ct)
    {
        // Self-filter: only deactivate tokens that belong to THIS rider.
        var token = await _db.PushTokens
            .FirstOrDefaultAsync(
                pt => pt.Token == cmd.Token && pt.UserId == cmd.UserId, ct);

        if (token is null) return; // idempotent

        token.IsActive  = false;
        token.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class RegisterRiderPushTokenValidator
    : AbstractValidator<RegisterRiderPushTokenCommand>
{
    private static readonly string[] AllowedPlatforms = ["ios", "android"];

    public RegisterRiderPushTokenValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(x => x.Platform)
            .NotEmpty()
            .Must(p => AllowedPlatforms.Contains(p))
            .WithMessage("platform must be 'ios' or 'android'.");
    }
}

public sealed class DeactivateRiderPushTokenValidator
    : AbstractValidator<DeactivateRiderPushTokenCommand>
{
    public DeactivateRiderPushTokenValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
