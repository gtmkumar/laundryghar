using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Commands.GenerateTags;

// ── Generate Tags ─────────────────────────────────────────────────────────────

public sealed record GenerateTagsCommand(GenerateTagsRequest Request, Guid? ActorId)
    : ICommand<IReadOnlyList<GarmentTagDto>>;

public class GenerateTagsCommandHandler : ICommandHandler<GenerateTagsCommand, IReadOnlyList<GarmentTagDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GenerateTagsCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<IReadOnlyList<GarmentTagDto>> HandleAsync(GenerateTagsCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Base for sequential codes
        var existing = await _db.FulfillmentUnitTags
            .Where(t => t.BrandId == brandId)
            .CountAsync(cancellationToken);

        var tags = Enumerable.Range(1, req.Count).Select(i => new FulfillmentUnitTag
        {
            Id          = Guid.NewGuid(),
            BrandId     = brandId,
            TagCode     = $"LG-{brandId.ToString()[..4].ToUpper()}-{(existing + i):D8}",
            TagFormat   = req.TagFormat,
            BatchNumber = req.BatchNumber,
            PrintedAt   = now,
            PrintedBy   = command.ActorId,
            Status      = "available",
            CreatedAt   = now,
            UpdatedAt   = now,
            CreatedBy   = command.ActorId
        }).ToList();

        _db.FulfillmentUnitTags.AddRange(tags);
        await _db.SaveChangesAsync(cancellationToken);

        return tags.Select(t => new GarmentTagDto(
            t.Id, t.BrandId, t.TagCode, t.TagFormat,
            t.BatchNumber, t.AssignedToFulfillmentUnitId,
            t.AssignedAt, t.IsDamaged, t.Status, t.CreatedAt)).ToList();
    }
}

public sealed class GenerateTagsRequestValidator : AbstractValidator<GenerateTagsRequest>
{
    private static readonly string[] AllowedFormats = ["qr","barcode_128","barcode_39","rfid"];

    public GenerateTagsRequestValidator()
    {
        RuleFor(x => x.Count).InclusiveBetween(1, 200);
        RuleFor(x => x.TagFormat)
            .Must(f => AllowedFormats.Contains(f))
            .WithMessage($"TagFormat must be one of: {string.Join(", ", AllowedFormats)}.");
    }
}
