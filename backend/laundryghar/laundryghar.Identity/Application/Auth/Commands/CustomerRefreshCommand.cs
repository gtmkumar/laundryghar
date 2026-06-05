using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record CustomerRefreshCommand(
    string RawRefreshToken,
    string? IpAddress,
    string? UserAgent
) : IRequest<CustomerTokenResponse>;

public sealed record CustomerLogoutCommand(string RawRefreshToken) : IRequest<Unit>;
