using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;
using operations.Application.Warehouse.Inspections.Commands.CreateInspection;
using operations.Application.Warehouse.Inspections.Commands.UploadInspectionPhoto;
using operations.Application.Warehouse.Inspections.Dtos;
using operations.Application.Warehouse.Inspections.Queries.GetGarmentInspections;
using operations.Application.Warehouse.Inspections.Queries.GetInspectionPhotos;
using operations.Application.Warehouse.Inspections.Queries.GetInspectionPhotoStream;

namespace operations.WebApi.Endpoints.Warehouse;

/// <summary>
/// Admin — Garment inspections + inspection photos. Thin dispatch through <see cref="IDispatcher"/>.
/// Photo upload is multipart (IFormFile) — requires <c>DisableAntiforgery()</c> in .NET minimal APIs
/// and a request-size cap (10 MB + envelope overhead). Photo streaming is by photo id to avoid
/// exposing raw storage keys in the API surface.
/// </summary>
public class WarehouseInspections : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/garment-inspections";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Inspections").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:fulfillment.inspect");

        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateInspectionRequest>>()
            .RequireAuthorization("permission:fulfillment.inspect");

        // POST /api/v1/admin/garment-inspections/{id}/photos — multipart upload.
        group.MapPost(UploadPhoto, "/{id:guid}/photos")
            .RequireAuthorization("permission:fulfillment.inspect")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024)); // 10 MB + overhead

        group.MapGet(GetPhotos, "/{id:guid}/photos").RequireAuthorization("permission:fulfillment.inspect");

        // GET /api/v1/admin/garment-inspections/photos/{photoId} — streams the image.
        group.MapGet(StreamPhoto, "/photos/{photoId:guid}").RequireAuthorization("permission:fulfillment.inspect");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        Guid? garmentId = null, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(
            new GetGarmentInspectionsQuery(garmentId ?? Guid.Empty, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<GarmentInspectionDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Create(CreateInspectionRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateInspectionCommand(req, user.UserId), ct);
        return Results.Created($"/api/v1/admin/garment-inspections/{data.Id}",
            new SingleResponse<GarmentInspectionDto> { Status = true, Data = data });
    }

    public static async Task<IResult> UploadPhoto(
        Guid id,
        IFormFile file,
        ICurrentUser user,
        IDispatcher dispatcher,
        CancellationToken ct,
        string view = "overall",
        bool isPrimary = false)
    {
        var data = await dispatcher.SendAsync(
            new UploadInspectionPhotoCommand(id, file, view, isPrimary, user.UserId), ct);
        return Results.Created(
            $"/api/v1/admin/garment-inspections/{id}/photos/{data.Id}",
            new SingleResponse<InspectionPhotoDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetPhotos(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var list = await dispatcher.QueryAsync(new GetInspectionPhotosQuery(id), ct);
        return Results.Ok(new ListResponse<InspectionPhotoDto> { Status = true, Data = list });
    }

    public static async Task<IResult> StreamPhoto(Guid photoId, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.QueryAsync(new GetInspectionPhotoStreamQuery(photoId), ct);
        if (result is null) return Results.NotFound();

        return Results.Stream(
            result.Stream,
            contentType: result.ContentType,
            fileDownloadName: result.FileName,
            enableRangeProcessing: false);
    }
}
