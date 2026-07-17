using core.Application.Engagement.Cms.OnboardingSlides.Commands.CreateOnboardingSlide;
using core.Application.Engagement.Cms.OnboardingSlides.Commands.DeleteOnboardingSlide;
using core.Application.Engagement.Cms.OnboardingSlides.Commands.UpdateOnboardingSlide;
using core.Application.Engagement.Cms.OnboardingSlides.Queries.GetOnboardingSlideById;
using core.Application.Engagement.Cms.OnboardingSlides.Queries.GetOnboardingSlides;
using core.Application.Engagement.Cms.Dtos;
using laundryghar.Utilities.Caching;
using laundryghar.Utilities.Validation;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Admin CMS — Onboarding Slide endpoints. Thin: each method dispatches a command/query through
/// <see cref="IDispatcher"/>. No business logic here.
/// </summary>
public class OnboardingSlides : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/onboarding-slides";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - CMS - Onboarding Slides")
             .RequireAuthorization("permission:cms.onboarding.manage")
             // Writes regenerate the cached public onboarding-slides response.
             .EvictOutputCacheOnWrite(CmsCacheTags.OnboardingSlides);

        group.MapGet(GetAll);
        group.MapGet(GetById, "{id:guid}");
        group.MapPost(Create).AddEndpointFilter<ValidationFilter<CreateOnboardingSlideRequest>>();
        group.MapPut(Update, "{id:guid}").AddEndpointFilter<ValidationFilter<UpdateOnboardingSlideRequest>>();
        group.MapDelete(Delete, "{id:guid}");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? appType = null)
    {
        var data = await dispatcher.QueryAsync(
            new GetOnboardingSlidesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, appType), ct);
        return Results.Ok(new PaginatedListResponse<OnboardingSlideDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetOnboardingSlideByIdQuery(id), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingSlideDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateOnboardingSlideRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateOnboardingSlideCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/onboarding-slides/{data.Id}",
            new SingleResponse<OnboardingSlideDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Update(Guid id, UpdateOnboardingSlideRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpdateOnboardingSlideCommand(id, req, user.UserId), ct);
        return data is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<OnboardingSlideDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteOnboardingSlideCommand(id, user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
