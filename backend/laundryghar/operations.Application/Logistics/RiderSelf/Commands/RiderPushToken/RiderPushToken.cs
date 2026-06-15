using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.RiderSelf.Commands.RiderPushToken;

// ── Register / upsert a rider Expo push token ─────────────────────────────────

public sealed record RegisterRiderPushTokenCommand(
    Guid   UserId,
    Guid   BrandId,
    string Token,
    string Platform) : ICommand<bool>;

public sealed class RegisterRiderPushTokenHandler
    : ICommandHandler<RegisterRiderPushTokenCommand, bool>
{
    private readonly IOperationsDbContext _db;

    public RegisterRiderPushTokenHandler(IOperationsDbContext db) => _db = db;

    public async Task<bool> HandleAsync(RegisterRiderPushTokenCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
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
        return true;
    }
}

// ── Deactivate a rider push token (logout) ────────────────────────────────────

public sealed record DeactivateRiderPushTokenCommand(
    Guid   UserId,
    string Token) : ICommand<bool>;

public sealed class DeactivateRiderPushTokenHandler
    : ICommandHandler<DeactivateRiderPushTokenCommand, bool>
{
    private readonly IOperationsDbContext _db;

    public DeactivateRiderPushTokenHandler(IOperationsDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeactivateRiderPushTokenCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;

        // Self-filter: only deactivate tokens that belong to THIS rider.
        var token = await _db.PushTokens
            .FirstOrDefaultAsync(
                pt => pt.Token == cmd.Token && pt.UserId == cmd.UserId, ct);

        if (token is null) return true; // idempotent

        token.IsActive  = false;
        token.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
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
