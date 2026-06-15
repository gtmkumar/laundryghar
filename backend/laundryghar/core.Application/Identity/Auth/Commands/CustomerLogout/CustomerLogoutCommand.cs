using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerLogout;

public sealed record CustomerLogoutCommand(string RawRefreshToken) : ICommand<bool>;
