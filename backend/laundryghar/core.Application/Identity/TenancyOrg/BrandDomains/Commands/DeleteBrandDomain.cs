using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.BrandDomains.Commands;

public sealed record DeleteBrandDomainCommand(Guid BrandId, Guid DomainId) : ICommand<bool>;

/// <summary>
/// Removes a custom domain. A hard delete, not a soft one: the row's whole purpose is to make a
/// hostname resolve, and <c>UNIQUE(domain)</c> is global — a soft-deleted row would keep the
/// hostname claimed forever and stop the provider (or anyone else) from ever re-adding it.
/// </summary>
public class DeleteBrandDomainCommandHandler : ICommandHandler<DeleteBrandDomainCommand, bool>
{
    private readonly ICoreDbContext _db;
    public DeleteBrandDomainCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteBrandDomainCommand cmd, CancellationToken ct)
    {
        // Scoped by BrandId as well as Id — no cross-tenant deletes by guessed id.
        var row = await _db.BrandDomains
            .FirstOrDefaultAsync(d => d.Id == cmd.DomainId && d.BrandId == cmd.BrandId, ct);

        if (row is null) return false;

        _db.BrandDomains.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
