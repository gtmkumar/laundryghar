using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Customer.Self.Commands;

// ── Register / upsert a customer Expo push token ──────────────────────────────

public sealed record RegisterCustomerPushTokenCommand(
    Guid   CustomerId,
    Guid   BrandId,
    string Token,
    string Platform) : ICommand<bool>;

public sealed class RegisterCustomerPushTokenHandler
    : ICommandHandler<RegisterCustomerPushTokenCommand, bool>
{
    private readonly IOperationsDbContext _db;

    public RegisterCustomerPushTokenHandler(IOperationsDbContext db) => _db = db;

    public async Task<bool> HandleAsync(RegisterCustomerPushTokenCommand cmd, CancellationToken ct)
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
        return true;
    }
}

// ── Deactivate a customer push token (logout) ─────────────────────────────────

public sealed record DeactivateCustomerPushTokenCommand(
    Guid   CustomerId,
    string Token) : ICommand<bool>;

public sealed class DeactivateCustomerPushTokenHandler
    : ICommandHandler<DeactivateCustomerPushTokenCommand, bool>
{
    private readonly IOperationsDbContext _db;

    public DeactivateCustomerPushTokenHandler(IOperationsDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeactivateCustomerPushTokenCommand cmd, CancellationToken ct)
    {
        // Self-filter: only deactivate tokens that belong to THIS customer.
        var token = await _db.PushTokens
            .FirstOrDefaultAsync(
                pt => pt.Token == cmd.Token && pt.CustomerId == cmd.CustomerId, ct);

        if (token is null) return true; // idempotent — already gone or never existed

        token.IsActive  = false;
        token.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

/// <summary>
/// Validates the push token registration command. Token non-empty, ≤ 4096 chars;
/// platform must be "ios" or "android". (Registered for assembly scan; the upload
/// route constructs the command in the endpoint, so no ValidationFilter is wired.)
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
