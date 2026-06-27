using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Commands.CreateInspection;

// ── Create Inspection ─────────────────────────────────────────────────────────

public sealed record CreateInspectionCommand(CreateInspectionRequest Request, Guid? ActorId)
    : ICommand<GarmentInspectionDto>;

public sealed class CreateInspectionCommandHandler : ICommandHandler<CreateInspectionCommand, GarmentInspectionDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateInspectionCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentInspectionDto> HandleAsync(CreateInspectionCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.FulfillmentUnits
            .FirstOrDefaultAsync(g => g.Id == req.FulfillmentUnitId && g.BrandId == brandId, cancellationToken)
            ?? throw new KeyNotFoundException($"FulfillmentUnit {req.FulfillmentUnitId} not found.");

        var inspection = new FulfillmentUnitInspection
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            FulfillmentUnitId             = req.FulfillmentUnitId,
            OrderId               = garment.OrderId,
            OrderCreatedAt        = garment.OrderCreatedAt,
            InspectedByUserId     = command.ActorId,
            InspectedByType       = req.InspectedByType,
            InspectionType        = req.InspectionType,
            InspectedAt           = now,
            Conditions            = req.Conditions,
            OverallCondition      = req.OverallCondition,
            IssuesCount           = (short)(req.Photos?.Length ?? 0),
            RequiresSpecialCare   = req.RequiresSpecialCare,
            Notes                 = req.Notes,
            CustomerAcknowledged  = false,
            CustomerOtpVerified   = false,
            QcResult              = req.QcResult,
            RewashCount           = garment.Attributes.RewashCount,
            Metadata              = "{}",
            CreatedAt             = now,
            CreatedBy             = command.ActorId
        };

        _db.FulfillmentUnitInspections.Add(inspection);

        var photos = new List<FulfillmentUnitInspectionPhoto>();
        if (req.Photos is { Length: > 0 })
        {
            foreach (var p in req.Photos)
            {
                var photo = new FulfillmentUnitInspectionPhoto
                {
                    Id           = Guid.NewGuid(),
                    InspectionId = inspection.Id,
                    FulfillmentUnitId    = req.FulfillmentUnitId,
                    BrandId      = brandId,
                    S3Key        = p.S3Key,
                    ThumbnailS3Key = p.ThumbnailS3Key,
                    View         = p.View,
                    Annotations  = "[]",
                    MimeType     = p.MimeType,
                    IsCompressed = false,
                    HasExif      = false,
                    CapturedAt   = now,
                    CapturedBy   = command.ActorId,
                    IsPrimary    = p.IsPrimary,
                    CreatedAt    = now,
                    CreatedBy    = command.ActorId
                };
                photos.Add(photo);
                _db.FulfillmentUnitInspectionPhotos.Add(photo);
            }
            inspection.IssuesCount = (short)photos.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(inspection, photos);
    }

    internal static GarmentInspectionDto ToDto(
        FulfillmentUnitInspection i, IEnumerable<FulfillmentUnitInspectionPhoto>? photos = null) => new(
        i.Id, i.BrandId, i.FulfillmentUnitId,
        i.InspectionType, i.InspectedByType,
        i.InspectedAt, i.OverallCondition,
        i.IssuesCount, i.RequiresSpecialCare,
        i.Notes, i.QcResult, i.CreatedAt,
        photos?.Select(p => new InspectionPhotoDto(
            p.Id, p.S3Key, p.View, p.MimeType, p.IsPrimary, p.CreatedAt)).ToList());
}

public sealed class CreateInspectionValidator : AbstractValidator<CreateInspectionRequest>
{
    private static readonly string[] AllowedInspectionTypes =
        ["pickup","intake","pre_wash","post_wash","qc","packing","delivery"];
    private static readonly string[] AllowedInspectedByTypes =
        ["rider","store_staff","warehouse_staff","qc_staff"];

    public CreateInspectionValidator()
    {
        RuleFor(x => x.FulfillmentUnitId).NotEmpty();
        RuleFor(x => x.InspectionType)
            .Must(t => AllowedInspectionTypes.Contains(t))
            .WithMessage($"InspectionType must be one of: {string.Join(", ", AllowedInspectionTypes)}.");
        RuleFor(x => x.InspectedByType)
            .Must(t => AllowedInspectedByTypes.Contains(t))
            .WithMessage($"InspectedByType must be one of: {string.Join(", ", AllowedInspectedByTypes)}.");
        RuleFor(x => x.Conditions).NotEmpty().WithMessage("Conditions JSON is required.");
    }
}
