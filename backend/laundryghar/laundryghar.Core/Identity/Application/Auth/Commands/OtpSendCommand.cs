using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record OtpSendCommand(
    string Identifier,
    string IdentifierType,
    string Purpose,
    string? IpAddress,
    string? UserAgent) : IRequest<OtpSentResponse>;
