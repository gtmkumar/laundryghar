using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerRefresh;

public sealed record CustomerRefreshCommand(
    string RawRefreshToken,
    string? IpAddress,
    string? UserAgent
) : ICommand<CustomerTokenResponse>;
