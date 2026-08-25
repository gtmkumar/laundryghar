using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerPhoneLink;

/// <summary>
/// Attaches a verified phone number to an already-authenticated customer — the second half
/// of the optional "add your mobile number" step after Google sign-up.
/// </summary>
/// <param name="CustomerId">Taken from the caller's own token, never from the request body.</param>
public sealed record CustomerPhoneLinkVerifyCommand(
    Guid CustomerId,
    string Phone,
    string Code,
    string? IpAddress,
    string? UserAgent
) : ICommand<CustomerPhoneLinkedResponse>;
