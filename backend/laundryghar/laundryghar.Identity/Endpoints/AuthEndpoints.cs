using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.Identity.Application.Auth.Dtos;
using MediatR;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// POST /api/v1/auth/password/login
/// POST /api/v1/auth/otp/send
/// POST /api/v1/auth/otp/verify
/// POST /api/v1/auth/refresh
/// POST /api/v1/auth/logout
/// POST /api/v1/auth/password/forgot
/// POST /api/v1/auth/password/reset
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        // C5: rate-limit the entire auth group (10 req / 60 s per client IP)
        var auth = group.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        // POST /api/v1/auth/password/login
        auth.MapPost("/password/login", async (
            PasswordLoginRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new PasswordLoginCommand(req.Identifier, req.Password, ip, ua), ct);
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .WithName("PasswordLogin")
        .Produces<SingleResponse<TokenResponse>>()
        .ProducesProblem(401)
        .AllowAnonymous();

        // POST /api/v1/auth/otp/send
        auth.MapPost("/otp/send", async (
            OtpSendRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new OtpSendCommand(req.Identifier, req.IdentifierType, req.Purpose, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .WithName("OtpSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/otp/verify
        auth.MapPost("/otp/verify", async (
            OtpVerifyRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new OtpVerifyCommand(req.Identifier, req.IdentifierType, req.Purpose, req.Code, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpVerifiedResponse> { Status = true, Data = result });
        })
        .WithName("OtpVerify")
        .Produces<SingleResponse<OtpVerifiedResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/refresh
        auth.MapPost("/refresh", async (
            RefreshTokenRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new RefreshTokenCommand(req.RefreshToken, ip, ua), ct);
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .WithName("RefreshToken")
        .Produces<SingleResponse<TokenResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/logout
        auth.MapPost("/logout", async (
            LogoutRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(req.RefreshToken), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Logged out successfully." } });
        })
        .WithName("Logout")
        .RequireAuthorization();

        // POST /api/v1/auth/password/forgot
        auth.MapPost("/password/forgot", async (
            ForgotPasswordRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new ForgotPasswordCommand(req.Identifier, req.IdentifierType), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "If an account exists, a reset link has been sent." } });
        })
        .WithName("ForgotPassword")
        .AllowAnonymous();

        // POST /api/v1/auth/password/reset
        auth.MapPost("/password/reset", async (
            ResetPasswordRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new ResetPasswordCommand(req.Token, req.NewPassword), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Password reset successfully." } });
        })
        .WithName("ResetPassword")
        .AllowAnonymous();

        return group;
    }
}
