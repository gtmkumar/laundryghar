using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.GoogleLogin;

/// <summary>
/// Staff/system login with a Google ID token, for admin-web and pos-web.
/// Unlike the customer flow this NEVER creates an account — the email must already belong
/// to a provisioned identity_access.users row.
/// </summary>
public sealed record GoogleLoginCommand(
    string IdToken,
    string? IpAddress,
    string? UserAgent
) : ICommand<TokenResponse>;
