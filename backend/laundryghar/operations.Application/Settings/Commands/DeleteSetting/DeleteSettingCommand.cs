using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Settings.Commands.DeleteSetting;

/// <summary>
/// Clears a single scope-level setting row so the key falls back to the next scope up the
/// precedence chain. Manage gate: permission:settings.manage. Same scope-boundary guard as the
/// upsert path. Returns false when there was no row to clear.
/// </summary>
public sealed record DeleteSettingCommand(
    string Category, string Key, string ScopeType, Guid? FranchiseId, Guid? StoreId)
    : ICommand<bool>;

public sealed class DeleteSettingHandler : ICommandHandler<DeleteSettingCommand, bool>
{
    private static readonly string[] WritableScopes = ["brand", "franchise", "store"];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteSettingHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteSettingCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        if (Array.IndexOf(WritableScopes, cmd.ScopeType) < 0)
            throw new BusinessRuleException($"scopeType must be one of: {string.Join(", ", WritableScopes)}.");

        Guid? franchiseId = cmd.ScopeType == "franchise" ? cmd.FranchiseId : null;
        Guid? storeId     = cmd.ScopeType == "store"     ? cmd.StoreId     : null;

        // Resolve the store's franchise for the boundary check (mirrors the upsert path).
        Guid? storeFranchiseId = null;
        if (storeId is Guid sid)
            storeFranchiseId = await _db.Stores.AsNoTracking()
                .Where(s => s.Id == sid && s.BrandId == brandId)
                .Select(s => (Guid?)s.FranchiseId)
                .FirstOrDefaultAsync(ct);

        if (!_user.IsWithinScope(brandId: brandId, franchiseId: franchiseId ?? storeFranchiseId, storeId: storeId))
            throw new ForbiddenException("The target scope is outside your assigned scope.");

        var row = await _db.SystemSettings.FirstOrDefaultAsync(
            s => s.ScopeType == cmd.ScopeType
              && s.BrandId == brandId
              && s.FranchiseId == franchiseId
              && s.StoreId == storeId
              && s.Category == cmd.Category
              && s.SettingKey == cmd.Key
              && s.Status == "active", ct);

        if (row is null) return false;

        _db.SystemSettings.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
