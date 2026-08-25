using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.Auth.Commands.CustomerPin;

/// <summary>
/// Stores an Argon2id hash of the caller's unlock PIN, reusing the same hasher as staff
/// passwords rather than inventing a second scheme.
///
/// A PIN is a low-entropy secret (10 000 combinations at four digits), so it is only ever
/// a SECOND factor for a device that already proved ownership of the phone or Google
/// account. Its brute-force resistance comes from the lockout in
/// <see cref="CustomerPinVerifyHandler"/>, not from the secret itself.
/// </summary>
public sealed class CustomerPinSetHandler : ICommandHandler<CustomerPinSetCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<CustomerPinSetHandler> _logger;

    public CustomerPinSetHandler(
        ICoreDbContext db,
        IPasswordHasher hasher,
        ILogger<CustomerPinSetHandler> logger)
    {
        _db     = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<bool> HandleAsync(CustomerPinSetCommand cmd, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cmd.CustomerId, ct)
            ?? throw new UnauthorizedAccessException("Customer not found.");

        customer.PinHash = _hasher.Hash(cmd.Pin);
        customer.PinSetAt = DateTimeOffset.UtcNow;
        // Setting a new PIN clears any standing lockout — the customer has just proved
        // possession of a valid session, which outranks a stale failed-attempt counter.
        customer.PinFailedAttempts = 0;
        customer.PinLockedUntil = null;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Customer {CustomerId} set an unlock PIN.", customer.Id);
        return true;
    }
}
