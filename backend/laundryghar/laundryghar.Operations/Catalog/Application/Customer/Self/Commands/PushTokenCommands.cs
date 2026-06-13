using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using MediatR;

namespace laundryghar.Catalog.Application.Customer.Self.Commands;

// ── Register / upsert a customer Expo push token ──────────────────────────────

public sealed record RegisterCustomerPushTokenCommand(
    Guid   CustomerId,
    Guid   BrandId,
    string Token,
    string Platform) : IRequest;

public sealed class RegisterCustomerPushTokenHandler
    : IRequestHandler<RegisterCustomerPushTokenCommand>
{
    private readonly LaundryGharDbContext _db;

    public RegisterCustomerPushTokenHandler(LaundryGharDbContext db) => _db = db;

    public async Task Handle(RegisterCustomerPushTokenCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Upsert on the unique token column.
        // Re-registering an existing token re-points it to this customer and re-activates.
        var existing = await _db.PushTokens
            .FirstOrDefaultAsync(pt => pt.Token == cmd.Token, ct);

        if (existing is not null)
        {
            existing.BrandId    = cmd.BrandId;
            existing.UserType   = "customer";
            existing.CustomerId = cmd.CustomerId;
            existing.UserId     = null;
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
                UserType   = "customer",
                CustomerId = cmd.CustomerId,
                UserId     = null,
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

// ── Deactivate a customer push token (logout) ─────────────────────────────────

public sealed record DeactivateCustomerPushTokenCommand(
    Guid   CustomerId,
    string Token) : IRequest;

public sealed class DeactivateCustomerPushTokenHandler
    : IRequestHandler<DeactivateCustomerPushTokenCommand>
{
    private readonly LaundryGharDbContext _db;

    public DeactivateCustomerPushTokenHandler(LaundryGharDbContext db) => _db = db;

    public async Task Handle(DeactivateCustomerPushTokenCommand cmd, CancellationToken ct)
    {
        // Self-filter: only deactivate tokens that belong to THIS customer.
        var token = await _db.PushTokens
            .FirstOrDefaultAsync(
                pt => pt.Token == cmd.Token && pt.CustomerId == cmd.CustomerId, ct);

        if (token is null) return; // idempotent — already gone or never existed

        token.IsActive  = false;
        token.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

/// <summary>
/// Validates the push token registration request.
/// Token must be non-empty and ≤ 4096 chars.
/// Platform must be "ios" or "android".
/// The Expo token format check (ExponentPushToken[/ExpoPushToken[) is a soft warning:
/// we allow other formats through without hard-failing (GCM direct tokens, etc.).
/// </summary>
public sealed class RegisterCustomerPushTokenValidator
    : AbstractValidator<RegisterCustomerPushTokenCommand>
{
    private static readonly string[] AllowedPlatforms = ["ios", "android"];

    public RegisterCustomerPushTokenValidator()
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

public sealed class DeactivateCustomerPushTokenValidator
    : AbstractValidator<DeactivateCustomerPushTokenCommand>
{
    public DeactivateCustomerPushTokenValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
