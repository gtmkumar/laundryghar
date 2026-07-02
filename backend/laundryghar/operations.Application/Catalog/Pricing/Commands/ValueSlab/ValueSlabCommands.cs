using System.Globalization;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Commands.ValueSlab;

// ── Create ────────────────────────────────────────────────────────────────────

public sealed record CreateValueSlabCommand(CreateValueSlabRequest Request, Guid? ActorId)
    : ICommand<ValueSlabDto>;

public sealed class CreateValueSlabHandler : ICommandHandler<CreateValueSlabCommand, ValueSlabDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public CreateValueSlabHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ValueSlabDto> HandleAsync(CreateValueSlabCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        await ValueSlabGuards.ValidateBoundsAndServiceAsync(_db, brandId, req.ServiceId, req.MinValue, req.MaxValue, ct);
        await ValueSlabResolver.EnsureNoOverlapAsync(_db, brandId, req.ServiceId, req.MinValue, req.MaxValue, null, ct);

        var e = new ValuePriceSlab
        {
            Id        = Guid.NewGuid(),
            BrandId   = brandId,
            ServiceId = req.ServiceId,
            MinValue  = req.MinValue,
            MaxValue  = req.MaxValue,
            Price     = req.Price,
            Status    = "active",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = cmd.ActorId,
            UpdatedBy = cmd.ActorId,
            Version   = 1,
        };
        _db.ValuePriceSlabs.Add(e);

        PricingChangeLogger.Add(_db, brandId, "value_price_slab", e.Id,
            $"Added value slab {Summ(e.MinValue, e.MaxValue)} → {e.Price:0.##}",
            before: new { }, after: Snapshot(e), cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return ToDto(e, null);
    }

    internal static ValueSlabDto ToDto(ValuePriceSlab e, string? serviceName) => new(
        e.Id, e.BrandId, e.ServiceId, serviceName, e.MinValue, e.MaxValue, e.Price,
        e.Status, e.CreatedAt, e.UpdatedAt);

    internal static object Snapshot(ValuePriceSlab e) => new
    {
        e.ServiceId, e.MinValue, e.MaxValue, e.Price, e.Status,
    };

    private static string Summ(decimal min, decimal? max) =>
        max is null
            ? $"{min.ToString("0.##", CultureInfo.InvariantCulture)}+"
            : $"{min.ToString("0.##", CultureInfo.InvariantCulture)}–{max.Value.ToString("0.##", CultureInfo.InvariantCulture)}";
}

// ── Update ────────────────────────────────────────────────────────────────────

public sealed record UpdateValueSlabCommand(Guid Id, UpdateValueSlabRequest Request, Guid? ActorId)
    : ICommand<ValueSlabDto?>;

public sealed class UpdateValueSlabHandler : ICommandHandler<UpdateValueSlabCommand, ValueSlabDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public UpdateValueSlabHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ValueSlabDto?> HandleAsync(UpdateValueSlabCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ValuePriceSlabs
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null) return null;

        var req = cmd.Request;
        if (req.Status is not ("active" or "inactive" or "archived"))
            throw new BusinessRuleException("Status must be one of: active, inactive, archived.");

        await ValueSlabGuards.ValidateBoundsAndServiceAsync(_db, brandId, req.ServiceId, req.MinValue, req.MaxValue, ct);
        // Only active slabs participate in resolution, so overlap only matters when staying active.
        if (req.Status == "active")
            await ValueSlabResolver.EnsureNoOverlapAsync(_db, brandId, req.ServiceId, req.MinValue, req.MaxValue, e.Id, ct);

        var before = CreateValueSlabHandler.Snapshot(e);
        e.ServiceId = req.ServiceId;
        e.MinValue  = req.MinValue;
        e.MaxValue  = req.MaxValue;
        e.Price     = req.Price;
        e.Status    = req.Status;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        e.Version++;

        PricingChangeLogger.Add(_db, brandId, "value_price_slab", e.Id,
            $"Updated value slab → {e.Price:0.##}",
            before, CreateValueSlabHandler.Snapshot(e), cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);

        var serviceName = e.ServiceId is { } sid
            ? await _db.Services.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;
        return CreateValueSlabHandler.ToDto(e, serviceName);
    }
}

// ── Delete (soft, via status) ─────────────────────────────────────────────────

public sealed record DeleteValueSlabCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteValueSlabHandler : ICommandHandler<DeleteValueSlabCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public DeleteValueSlabHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteValueSlabCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ValuePriceSlabs
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.Status == "archived") return false;

        var before = CreateValueSlabHandler.Snapshot(e);
        // Soft-retire: 'archived' drops it out of resolution and overlap checks.
        e.Status    = "archived";
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        e.Version++;

        PricingChangeLogger.Add(_db, brandId, "value_price_slab", e.Id,
            "Archived value slab", before, CreateValueSlabHandler.Snapshot(e), cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Shared authoring guards ───────────────────────────────────────────────────

internal static class ValueSlabGuards
{
    public static async Task ValidateBoundsAndServiceAsync(
        IOperationsDbContext db, Guid brandId, Guid? serviceId,
        decimal minValue, decimal? maxValue, CancellationToken ct)
    {
        if (minValue < 0m)
            throw new BusinessRuleException("Minimum value cannot be negative.");
        if (maxValue is { } mx && mx <= minValue)
            throw new BusinessRuleException("Maximum value must be greater than the minimum value.");

        if (serviceId is { } sid)
        {
            var ok = await db.Services.AsNoTracking()
                .AnyAsync(s => s.Id == sid && s.BrandId == brandId, ct);
            if (!ok) throw new KeyNotFoundException("Service not found in brand.");
        }
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class CreateValueSlabValidator : AbstractValidator<CreateValueSlabRequest>
{
    public CreateValueSlabValidator()
    {
        RuleFor(x => x.MinValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxValue!.Value).GreaterThan(x => x.MinValue)
            .When(x => x.MaxValue.HasValue)
            .WithMessage("Maximum value must be greater than the minimum value.");
    }
}

public sealed class UpdateValueSlabValidator : AbstractValidator<UpdateValueSlabRequest>
{
    public UpdateValueSlabValidator()
    {
        RuleFor(x => x.MinValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxValue!.Value).GreaterThan(x => x.MinValue)
            .When(x => x.MaxValue.HasValue)
            .WithMessage("Maximum value must be greater than the minimum value.");
        RuleFor(x => x.Status).Must(s => s is "active" or "inactive" or "archived")
            .WithMessage("Status must be one of: active, inactive, archived.");
    }
}
