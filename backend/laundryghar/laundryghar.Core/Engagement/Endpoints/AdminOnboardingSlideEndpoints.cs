using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

public static class AdminOnboardingSlideEndpoints
{
    public static RouteGroupBuilder MapAdminOnboardingSlideEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/onboarding-slides")
            .WithTags("Admin - CMS - Onboarding Slides");

        g.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? appType = null) =>
        {
            var r = await sender.Send(new GetOnboardingSlidesQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, appType), ct);
            return Results.Ok(new PaginatedListResponse<OnboardingSlideDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.onboarding.manage");

        g.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetOnboardingSlideByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<OnboardingSlideDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.onboarding.manage");

        g.MapPost("/", async (CreateOnboardingSlideRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateOnboardingSlideCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/onboarding-slides/{r.Id}",
                new SingleResponse<OnboardingSlideDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.onboarding.manage");

        g.MapPut("/{id:guid}", async (Guid id, UpdateOnboardingSlideRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateOnboardingSlideCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<OnboardingSlideDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cms.onboarding.manage");

        g.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new DeleteOnboardingSlideCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:cms.onboarding.manage");

        return group;
    }
}
