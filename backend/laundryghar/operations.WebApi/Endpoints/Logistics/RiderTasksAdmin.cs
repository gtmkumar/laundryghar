using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Logistics.RiderSelf.Queries.GetProofPhotoStream;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider tasks: streams a rider's proof-of-delivery photo for dispatch inspection.
/// Routed under /rider-tasks to avoid confusion with the /rider-assignments CRUD group.
/// Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class RiderTasksAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/rider-tasks";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Tasks").RequireAuthorization();

        group.MapGet(StreamProofPhoto, "/{id:guid}/proof-photo").RequireAuthorization("permission:rider.read");
    }

    public static async Task<IResult> StreamProofPhoto(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var brandId = u.RequireBrandId();
        var result  = await dispatcher.QueryAsync(new GetProofPhotoStreamQuery(id, brandId), ct);
        if (result is null) return Results.NotFound();

        return Results.Stream(
            result.Stream,
            contentType: result.ContentType,
            fileDownloadName: result.FileName,
            enableRangeProcessing: false);
    }
}
