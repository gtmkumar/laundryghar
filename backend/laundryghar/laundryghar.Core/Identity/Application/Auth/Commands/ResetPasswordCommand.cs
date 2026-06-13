using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Unit>;
