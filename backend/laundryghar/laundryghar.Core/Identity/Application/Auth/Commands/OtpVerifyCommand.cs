using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record OtpVerifyCommand(
    string Identifier,
    string IdentifierType,
    string Purpose,
    string Code,
    string? IpAddress,
    string? UserAgent) : IRequest<OtpVerifiedResponse>;
