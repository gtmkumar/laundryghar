using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.PartnerOtpVerify;

/// <summary>
/// Verifies a RaaS partner login OTP (purpose=partner_login) and, on success, mints a
/// <c>token_use=partner</c> access token for the matching partner user. Distinct from the
/// customer/step-up verify flows: no refresh token, no login-history row, no brand context —
/// the partner is a separate actor isolated only by <c>partner_id</c> (docs/rbac.md §9).
/// The phone is client-supplied (this is a login), unlike step-up which derives it from the token.
/// </summary>
public sealed record PartnerOtpVerifyCommand(
    string Phone,
    string Code
) : ICommand<PartnerTokenResponse>;
