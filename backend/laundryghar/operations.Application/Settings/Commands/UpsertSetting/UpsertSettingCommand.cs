using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;
using operations.Application.Settings.Dtos;
using ValidationException = laundryghar.Utilities.Exceptions.ValidationException;

namespace operations.Application.Settings.Commands.UpsertSetting;

/// <summary>
/// Creates, updates, or clears a single scope-level setting row. Manage gate: permission:settings.manage.
/// Enforces §6 scope boundary (a franchise/store writer can only touch its own subtree), the brand's
/// validation-schema clamp for franchise/store writes, and version bumps on update. A null/blank value
/// clears (deletes) the row for that exact scope.
/// </summary>
public sealed record UpsertSettingCommand(UpsertSettingRequest Request, Guid? ActorId)
    : ICommand<SettingRowDto?>;

public sealed class UpsertSettingHandler : ICommandHandler<UpsertSettingCommand, SettingRowDto?>
{
    private static readonly string[] WritableScopes = ["brand", "franchise", "store"];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpsertSettingHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<SettingRowDto?> HandleAsync(UpsertSettingCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;

        if (Array.IndexOf(WritableScopes, req.ScopeType) < 0)
            throw new BusinessRuleException($"scopeType must be one of: {string.Join(", ", WritableScopes)}.");

        // Normalise scope identity so upsert-find is deterministic against the unique index
        // (scope_type, brand_id, franchise_id, store_id, category, setting_key):
        //   brand     → (null, null)
        //   franchise → (franchiseId, null)
        //   store     → (null, storeId)   — a store row is keyed by store_id alone.
        Guid? franchiseId = req.ScopeType == "franchise" ? req.FranchiseId : null;
        Guid? storeId     = req.ScopeType == "store"     ? req.StoreId     : null;

        if (req.ScopeType == "franchise" && franchiseId is null)
            throw new BusinessRuleException("franchiseId is required for a franchise-scope setting.");
        if (req.ScopeType == "store" && storeId is null)
            throw new BusinessRuleException("storeId is required for a store-scope setting.");

        // ── Scope boundary + tenant-ownership guard ─────────────────────────────
        // Verify the target franchise/store belongs to this brand (cross-brand IDOR guard — RLS
        // does not cover system_settings) and lies within the caller's assigned scope subtree.
        Guid? storeFranchiseId = null;
        if (storeId is Guid sid)
        {
            storeFranchiseId = await _db.Stores.AsNoTracking()
                .Where(s => s.Id == sid && s.BrandId == brandId)
                .Select(s => (Guid?)s.FranchiseId)
                .FirstOrDefaultAsync(ct);
            if (storeFranchiseId is null)
                throw new KeyNotFoundException("Store not found in brand.");
        }
        if (franchiseId is Guid fid)
        {
            var franchiseInBrand = await _db.Franchises.AsNoTracking()
                .AnyAsync(f => f.Id == fid && f.BrandId == brandId, ct);
            if (!franchiseInBrand)
                throw new KeyNotFoundException("Franchise not found in brand.");
        }

        if (!_user.IsWithinScope(brandId: brandId, franchiseId: franchiseId ?? storeFranchiseId, storeId: storeId))
            throw new ForbiddenException("The target scope is outside your assigned scope.");

        // ── Clear semantics: null/blank value deletes this scope's row ───────────
        if (string.IsNullOrWhiteSpace(req.Value))
        {
            var toDelete = await FindRowAsync(_db, brandId, req.ScopeType, franchiseId, storeId, req.Category, req.Key, ct);
            if (toDelete is not null)
            {
                _db.SystemSettings.Remove(toDelete);
                await _db.SaveChangesAsync(ct);
            }
            return null;
        }

        // ── Encode the scalar into a valid jsonb literal (format-checked) ────────
        if (!SettingValueCodec.IsScalar(req.DataType))
            throw new BusinessRuleException($"dataType must be one of: {string.Join(", ", SettingValueCodec.ScalarDataTypes)}.");

        string encodedValue;
        try { encodedValue = SettingValueCodec.Encode(req.Value, req.DataType); }
        catch (FormatException ex) { throw new ValidationException(new Dictionary<string, string[]> { ["value"] = [ex.Message] }); }

        // ── Validation schema: only HQ (brand scope) may set/replace it ──────────
        string? validationSchema = null;
        if (req.ScopeType == "brand")
        {
            validationSchema = string.IsNullOrWhiteSpace(req.ValidationSchema) ? null : req.ValidationSchema;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(req.ValidationSchema))
                throw new ForbiddenException("Only a brand-scope (HQ) writer may set a validationSchema.");

            // Clamp: a lower scope's value must satisfy the brand row's validation band, if any.
            var brandRow = await FindRowAsync(_db, brandId, "brand", null, null, req.Category, req.Key, ct);
            var reason = SettingClampSchema.Validate(
                SettingClampSchema.Parse(brandRow?.ValidationSchema), req.Value, req.DataType);
            if (reason is not null)
                throw new ValidationException(new Dictionary<string, string[]> { ["value"] = [reason] });
        }

        // ── Upsert ───────────────────────────────────────────────────────────────
        var now = DateTimeOffset.UtcNow;
        var existing = await FindRowAsync(_db, brandId, req.ScopeType, franchiseId, storeId, req.Category, req.Key, ct);
        SystemSetting row;
        if (existing is null)
        {
            row = new SystemSetting
            {
                Id               = Guid.NewGuid(),
                ScopeType        = req.ScopeType,
                BrandId          = brandId,           // non-platform rows carry the owning brand
                FranchiseId      = franchiseId,
                StoreId          = storeId,
                Category         = req.Category,
                SettingKey       = req.Key,
                SettingValue     = encodedValue,
                DataType         = req.DataType,
                ValidationSchema = validationSchema,
                Status           = "active",
                Version          = 1,
                CreatedAt        = now,
                UpdatedAt        = now,
                CreatedBy        = cmd.ActorId,
                UpdatedBy        = cmd.ActorId,
            };
            _db.SystemSettings.Add(row);
        }
        else
        {
            existing.SettingValue = encodedValue;
            existing.DataType     = req.DataType;
            // Brand writers may replace the clamp; lower scopes never reach this branch with a schema.
            if (req.ScopeType == "brand") existing.ValidationSchema = validationSchema;
            existing.UpdatedAt = now;
            existing.UpdatedBy = cmd.ActorId;
            existing.Version++;
            row = existing;
        }

        await _db.SaveChangesAsync(ct);

        return new SettingRowDto(
            row.Id, row.Category, row.SettingKey, row.ScopeType,
            row.FranchiseId, row.StoreId, row.SettingValue, row.DataType,
            row.ValidationSchema, row.Version, row.UpdatedAt);
    }

    private static Task<SystemSetting?> FindRowAsync(
        IOperationsDbContext db, Guid brandId, string scopeType, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
        => db.SystemSettings.FirstOrDefaultAsync(
            s => s.ScopeType == scopeType
              && s.BrandId == brandId
              && s.FranchiseId == franchiseId
              && s.StoreId == storeId
              && s.Category == category
              && s.SettingKey == key
              && s.Status == "active", ct);
}

public sealed class UpsertSettingValidator : AbstractValidator<UpsertSettingCommand>
{
    public UpsertSettingValidator()
    {
        RuleFor(x => x.Request.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Key).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.ScopeType).NotEmpty();
        RuleFor(x => x.Request.DataType).NotEmpty().MaximumLength(20);
    }
}
