using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
using FluentValidation;
using laundryghar.Warehouse.Application.Inspections.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Inspections.Commands;

// ── Create Inspection ─────────────────────────────────────────────────────────

public sealed record CreateInspectionCommand(CreateInspectionRequest Request, Guid? ActorId)
    : IRequest<GarmentInspectionDto>;

public sealed class CreateInspectionHandler : IRequestHandler<CreateInspectionCommand, GarmentInspectionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateInspectionHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentInspectionDto> Handle(CreateInspectionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == req.GarmentId && g.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Garment {req.GarmentId} not found.");

        var inspection = new GarmentInspection
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            GarmentId             = req.GarmentId,
            OrderId               = garment.OrderId,
            OrderCreatedAt        = garment.OrderCreatedAt,
            InspectedByUserId     = cmd.ActorId,
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
            RewashCount           = garment.RewashCount,
            Metadata              = "{}",
            CreatedAt             = now,
            CreatedBy             = cmd.ActorId
        };

        _db.GarmentInspections.Add(inspection);

        var photos = new List<GarmentInspectionPhoto>();
        if (req.Photos is { Length: > 0 })
        {
            foreach (var p in req.Photos)
            {
                var photo = new GarmentInspectionPhoto
                {
                    Id           = Guid.NewGuid(),
                    InspectionId = inspection.Id,
                    GarmentId    = req.GarmentId,
                    BrandId      = brandId,
                    S3Key        = p.S3Key,
                    ThumbnailS3Key = p.ThumbnailS3Key,
                    View         = p.View,
                    Annotations  = "[]",
                    MimeType     = p.MimeType,
                    IsCompressed = false,
                    HasExif      = false,
                    CapturedAt   = now,
                    CapturedBy   = cmd.ActorId,
                    IsPrimary    = p.IsPrimary,
                    CreatedAt    = now,
                    CreatedBy    = cmd.ActorId
                };
                photos.Add(photo);
                _db.GarmentInspectionPhotos.Add(photo);
            }
            inspection.IssuesCount = (short)photos.Count;
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(inspection, photos);
    }

    internal static GarmentInspectionDto ToDto(
        GarmentInspection i, IEnumerable<GarmentInspectionPhoto>? photos = null) => new(
        i.Id, i.BrandId, i.GarmentId,
        i.InspectionType, i.InspectedByType,
        i.InspectedAt, i.OverallCondition,
        i.IssuesCount, i.RequiresSpecialCare,
        i.Notes, i.QcResult, i.CreatedAt,
        photos?.Select(p => new InspectionPhotoDto(
            p.Id, p.S3Key, p.View, p.MimeType, p.IsPrimary, p.CreatedAt)).ToList());
}

public sealed class CreateInspectionValidator : AbstractValidator<CreateInspectionCommand>
{
    private static readonly string[] AllowedInspectionTypes =
        ["pickup","intake","pre_wash","post_wash","qc","packing","delivery"];
    private static readonly string[] AllowedInspectedByTypes =
        ["rider","store_staff","warehouse_staff","qc_staff"];

    public CreateInspectionValidator()
    {
        RuleFor(x => x.Request.GarmentId).NotEmpty();
        RuleFor(x => x.Request.InspectionType)
            .Must(t => AllowedInspectionTypes.Contains(t))
            .WithMessage($"InspectionType must be one of: {string.Join(", ", AllowedInspectionTypes)}.");
        RuleFor(x => x.Request.InspectedByType)
            .Must(t => AllowedInspectedByTypes.Contains(t))
            .WithMessage($"InspectedByType must be one of: {string.Join(", ", AllowedInspectedByTypes)}.");
        RuleFor(x => x.Request.Conditions).NotEmpty().WithMessage("Conditions JSON is required.");
    }
}

// ── Garment Condition CRUD ─────────────────────────────────────────────────────

public sealed record CreateGarmentConditionCommand(CreateGarmentConditionRequest Request, Guid? ActorId)
    : IRequest<GarmentConditionDto>;

public sealed class CreateGarmentConditionHandler
    : IRequestHandler<CreateGarmentConditionCommand, GarmentConditionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateGarmentConditionHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentConditionDto> Handle(CreateGarmentConditionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new GarmentCondition
        {
            Id                 = Guid.NewGuid(),
            BrandId            = brandId,
            Code               = req.Code,
            Name               = req.Name,
            NameLocalized      = req.NameLocalized,
            Category           = req.Category,
            SeverityLevels     = ["minor","moderate","severe"],
            RequiresDisclaimer = req.RequiresDisclaimer,
            DisclaimerText     = req.DisclaimerText,
            DisplayOrder       = req.DisplayOrder,
            IsActive           = true,
            Status             = "active",
            CreatedAt          = now,
            UpdatedAt          = now,
            CreatedBy          = cmd.ActorId,
            UpdatedBy          = cmd.ActorId
        };

        _db.GarmentConditions.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static GarmentConditionDto ToDto(GarmentCondition c) => new(
        c.Id, c.BrandId, c.Code, c.Name, c.Category,
        c.RequiresDisclaimer, c.DisplayOrder, c.IsActive, c.Status);
}

public sealed record UpdateGarmentConditionCommand(
    Guid Id, UpdateGarmentConditionRequest Request, Guid? ActorId)
    : IRequest<GarmentConditionDto?>;

public sealed class UpdateGarmentConditionHandler
    : IRequestHandler<UpdateGarmentConditionCommand, GarmentConditionDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateGarmentConditionHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentConditionDto?> Handle(UpdateGarmentConditionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.GarmentConditions
            .FirstOrDefaultAsync(c => c.Id == cmd.Id && c.BrandId == brandId, ct);
        if (e is null) return null;

        var req = cmd.Request;
        e.Name               = req.Name;
        e.NameLocalized      = req.NameLocalized;
        e.RequiresDisclaimer = req.RequiresDisclaimer;
        e.DisclaimerText     = req.DisclaimerText;
        e.DisplayOrder       = req.DisplayOrder;
        e.Status             = req.Status;
        e.IsActive           = req.Status == "active";
        e.UpdatedAt          = DateTimeOffset.UtcNow;
        e.UpdatedBy          = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateGarmentConditionHandler.ToDto(e);
    }
}

public sealed class CreateGarmentConditionValidator : AbstractValidator<CreateGarmentConditionCommand>
{
    private static readonly string[] AllowedCategories =
        ["stain","damage","wear","missing_part","dimensional","color","other"];

    public CreateGarmentConditionValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty();
        RuleFor(x => x.Request.Category)
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}.");
    }
}
