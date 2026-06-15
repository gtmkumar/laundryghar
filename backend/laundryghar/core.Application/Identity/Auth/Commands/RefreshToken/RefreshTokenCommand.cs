using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.RefreshToken;

public sealed record RefreshTokenCommand(
    string RawRefreshToken,
    string? IpAddress,
    string? UserAgent) : ICommand<TokenResponse>;
