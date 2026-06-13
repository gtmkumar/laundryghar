using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <param name="ResolvedBrandId">Brand ID resolved before the command is dispatched (from header, body, or default config).</param>
public sealed record CustomerOtpSendCommand(
    string Phone,
    Guid ResolvedBrandId,
    string? IpAddress,
    string? UserAgent
) : IRequest<OtpSentResponse>;
