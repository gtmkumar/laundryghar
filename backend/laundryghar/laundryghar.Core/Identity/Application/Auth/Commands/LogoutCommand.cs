using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record LogoutCommand(string RawRefreshToken) : IRequest<Unit>;
