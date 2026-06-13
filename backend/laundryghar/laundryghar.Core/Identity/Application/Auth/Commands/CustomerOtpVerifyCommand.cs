using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

public sealed record CustomerOtpVerifyCommand(
    string Phone,
    string Code,
    Guid ResolvedBrandId,
    string? IpAddress,
    string? UserAgent
) : IRequest<CustomerTokenResponse>;
