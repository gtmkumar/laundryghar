using laundryghar.Warehouse.Infrastructure.Auth;
using laundryghar.Warehouse.Infrastructure.Services;
namespace laundryghar.Warehouse.Application.Inspections.Dtos;

public sealed record GarmentInspectionDto(
    Guid Id, Guid BrandId, Guid GarmentId,
    string InspectionType, string InspectedByType,
    DateTimeOffset InspectedAt, string? OverallCondition,
    short IssuesCount, bool RequiresSpecialCare,
    string? Notes, string? QcResult, DateTimeOffset CreatedAt,
    IReadOnlyList<InspectionPhotoDto>? Photos
);

public sealed record CreateInspectionRequest(
    Guid GarmentId,
    string InspectionType,
    string InspectedByType,
    string? OverallCondition,
    string Conditions,       // JSON array — validated as non-empty string
    bool RequiresSpecialCare,
    string? Notes,
    string? QcResult,
    CreateInspectionPhotoRequest[]? Photos
);

public sealed record CreateInspectionPhotoRequest(
    string S3Key, string View, string? ThumbnailS3Key,
    string MimeType, bool IsPrimary
);

public sealed record InspectionPhotoDto(
    Guid Id, string S3Key, string View,
    string MimeType, bool IsPrimary, DateTimeOffset CreatedAt
);

public sealed record GarmentConditionDto(
    Guid Id, Guid BrandId, string Code, string Name,
    string Category, bool RequiresDisclaimer,
    short DisplayOrder, bool IsActive, string Status
);

public sealed record CreateGarmentConditionRequest(
    string Code, string Name, string NameLocalized,
    string Category, bool RequiresDisclaimer, string? DisclaimerText,
    short DisplayOrder
);

public sealed record UpdateGarmentConditionRequest(
    string Name, string NameLocalized,
    bool RequiresDisclaimer, string? DisclaimerText,
    short DisplayOrder, string Status
);
