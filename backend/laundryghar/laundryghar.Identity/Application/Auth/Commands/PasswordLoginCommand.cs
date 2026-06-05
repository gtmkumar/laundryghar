using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record PasswordLoginCommand(
    string Identifier,
    string Password,
    string? IpAddress,
    string? UserAgent) : IRequest<TokenResponse>;
