using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerPin;

/// <summary>
/// Sets (or replaces) the caller's unlock PIN.
/// </summary>
/// <param name="CustomerId">Taken from the caller's own token, never from the request body.</param>
public sealed record CustomerPinSetCommand(
    Guid CustomerId,
    string Pin
) : ICommand<bool>;

/// <summary>
/// Unlocks with a PIN instead of a fresh OTP — the returning-user path.
/// </summary>
/// <param name="Identifier">Phone (E.164) or email of the account being unlocked.</param>
public sealed record CustomerPinVerifyCommand(
    string Identifier,
    string Pin,
    Guid? RawHeaderBrandId,
    string? BodyBrandCode,
    string? IpAddress,
    string? UserAgent
) : ICommand<CustomerTokenResponse>;
