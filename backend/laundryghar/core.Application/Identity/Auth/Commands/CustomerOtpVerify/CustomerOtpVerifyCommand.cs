using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.CustomerOtpVerify;

/// <summary>
/// Verifies a customer login OTP. Brand is resolved inside the handler from the raw
/// inputs (X-Brand-Id header → body brandCode → CustomerAuth:DefaultBrandCode config → "LG-MAIN").
/// </summary>
/// <param name="RawHeaderBrandId">Value of the X-Brand-Id header, if a valid Guid was present.</param>
/// <param name="BodyBrandCode">Optional brandCode from the request body.</param>
public sealed record CustomerOtpVerifyCommand(
    string Phone,
    string Code,
    Guid? RawHeaderBrandId,
    string? BodyBrandCode,
    string? IpAddress,
    string? UserAgent
) : ICommand<CustomerTokenResponse>;
