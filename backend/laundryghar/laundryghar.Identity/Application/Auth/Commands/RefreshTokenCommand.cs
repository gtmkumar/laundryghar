using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record RefreshTokenCommand(
    string RawRefreshToken,
    string? IpAddress,
    string? UserAgent) : IRequest<TokenResponse>;
