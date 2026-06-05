using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record ForgotPasswordCommand(string Identifier, string IdentifierType) : IRequest<Unit>;
