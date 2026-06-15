using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.Riders.Commands.Verification;
using operations.Application.Logistics.Riders.Dtos;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider documents review: stream the document image, approve / reject a single
/// KYC document. Sibling of the /riders group. Thin dispatch through <see cref="IDispatcher"/>.
/// </summary>
public class RiderDocumentsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/rider-documents";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Documents").RequireAuthorization();

        group.MapGet(StreamFile, "/{docId:guid}/file").RequireAuthorization("permission:rider.read");
        group.MapPost(Approve, "/{docId:guid}/approve").RequireAuthorization("permission:rider.verify");
        group.MapPost(Reject, "/{docId:guid}/reject")
            .AddEndpointFilter<ValidationFilter<RejectRiderRequest>>()
            .RequireAuthorization("permission:rider.verify");
    }

    public static async Task<IResult> StreamFile(Guid docId, IDispatcher dispatcher, CancellationToken ct)
    {
        var s = await dispatcher.QueryAsync(new GetRiderDocumentStreamQuery(docId), ct);
        return s is null ? Results.NotFound()
            : Results.Stream(s.Stream, contentType: s.ContentType, fileDownloadName: s.FileName, enableRangeProcessing: false);
    }

    public static async Task<IResult> Approve(Guid docId, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewRiderDocumentCommand(docId, true, null, u.UserId), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDocumentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Reject(Guid docId, RejectRiderRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ReviewRiderDocumentCommand(docId, false, req.Reason, u.UserId), ct);
        return r is null ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderDocumentDto> { Status = true, Data = r });
    }
}
