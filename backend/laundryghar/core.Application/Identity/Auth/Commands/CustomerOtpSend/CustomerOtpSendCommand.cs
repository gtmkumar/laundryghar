using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;

namespace core.Application.Identity.Auth.Commands.CustomerOtpSend;

/// <summary>
/// Sends a customer login OTP. Brand is resolved inside the handler from the raw
/// inputs (X-Brand-Id header → body brandCode → CustomerAuth:DefaultBrandCode config → "LG-MAIN").
/// </summary>
/// <param name="RawHeaderBrandId">Value of the X-Brand-Id header, if a valid Guid was present.</param>
/// <param name="BodyBrandCode">Optional brandCode from the request body.</param>
/// <param name="Purpose">
/// OTP namespace. <c>login</c> (default) for sign-in; <c>verify_phone</c> when an
/// already-authenticated customer is attaching a number to a Google-created account.
/// Keeping them separate means a pending link OTP cannot be redeemed as a login, and the
/// per-purpose cooldown/lockout windows do not collide.
/// </param>
/// <param name="AuthenticatedCustomerId">
/// Set only when an already-signed-in customer is requesting the OTP (the phone-link flow).
/// The brand is then taken from that customer's own row instead of resolved from a brand
/// code — which is both more correct (the caller's brand is not up for negotiation) and
/// necessary, because tenancy_org.brands is readable only under the pre-auth RLS bypass
/// that authenticated requests deliberately do not get.
/// </param>
public sealed record CustomerOtpSendCommand(
    string Phone,
    Guid? RawHeaderBrandId,
    string? BodyBrandCode,
    string? IpAddress,
    string? UserAgent,
    string Purpose = OtpPurpose.Login,
    Guid? AuthenticatedCustomerId = null
) : ICommand<OtpSentResponse>;
