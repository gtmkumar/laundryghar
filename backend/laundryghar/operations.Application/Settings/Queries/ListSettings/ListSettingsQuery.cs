using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;
using operations.Application.Settings.Dtos;

namespace operations.Application.Settings.Queries.ListSettings;

/// <summary>
/// Lists the scope-aware business-rule settings for a category: the raw rows visible to the caller
/// (platform default + brand + the requested franchise/store) and the effective value per key
/// resolved for the requested target scope. Read gate: permission:settings.read.
/// </summary>
public sealed record ListSettingsQuery(string Category, Guid? FranchiseId, Guid? StoreId)
    : IQuery<SettingsListDto>;

public sealed class ListSettingsHandler : IQueryHandler<ListSettingsQuery, SettingsListDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public ListSettingsHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<SettingsListDto> HandleAsync(ListSettingsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // A franchise/store-scoped caller may only inspect rows within their own subtree.
        if ((q.FranchiseId.HasValue || q.StoreId.HasValue)
            && !_user.IsWithinScope(brandId: brandId, franchiseId: q.FranchiseId, storeId: q.StoreId))
            throw new ForbiddenException("The requested scope is outside your assigned scope.");

        // Raw visible rows: platform default, brand, and the requested franchise/store (if any).
        var rows = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == q.Category
                     && s.Status == "active"
                     && (
                          (s.ScopeType == "platform"  && s.BrandId == null)
                       || (s.ScopeType == "brand"     && s.BrandId == brandId)
                       || (s.ScopeType == "franchise" && s.FranchiseId == q.FranchiseId)
                       || (s.ScopeType == "store"     && s.StoreId == q.StoreId)
                        ))
            .OrderBy(s => s.SettingKey)
            .Select(s => new SettingRowDto(
                s.Id, s.Category, s.SettingKey, s.ScopeType,
                s.FranchiseId, s.StoreId, s.SettingValue, s.DataType,
                s.ValidationSchema, s.Version, s.UpdatedAt))
            .ToListAsync(ct);

        // Effective per key for the requested target scope (one query over the distinct keys).
        var keys = rows.Select(r => r.Key).Distinct().ToArray();
        var effective = new List<EffectiveSettingDto>();
        if (keys.Length > 0)
        {
            var eff = await SettingsResolver.GetEffectiveBatchAsync(
                _db, brandId, q.FranchiseId, q.StoreId, q.Category, keys, ct);
            effective = eff
                .Select(kv => new EffectiveSettingDto(kv.Key, kv.Value.Value, kv.Value.DataType, kv.Value.SourceScope))
                .OrderBy(e => e.Key)
                .ToList();
        }

        return new SettingsListDto(rows, effective);
    }
}
