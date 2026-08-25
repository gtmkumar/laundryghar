using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerGoogleSignIn;

/// <summary>
/// Signs a customer in with a Google ID token, creating the account on first use.
/// Brand is resolved inside the handler (X-Brand-Id header → body brandCode →
/// CustomerAuth:DefaultBrandCode config → "LG-MAIN").
/// </summary>
/// <param name="IdToken">A Google-issued OpenID Connect ID token.</param>
public sealed record CustomerGoogleSignInCommand(
    string IdToken,
    Guid? RawHeaderBrandId,
    string? BodyBrandCode,
    string? IpAddress,
    string? UserAgent
) : ICommand<CustomerTokenResponse>;
