using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

/// <summary>Mark a brand-platform invoice paid or void (operator action; gated by saas.manage).
/// Used to record manual/offline payments until the gateway charger lands. Returns false if the
/// invoice isn't found; throws on an invalid target or a void (terminal) invoice.</summary>
public sealed record SetBrandPlatformInvoiceStatusCommand(Guid InvoiceId, SetInvoiceStatusRequest Request, Guid? ActorId)
    : ICommand<bool>;

public class SetBrandPlatformInvoiceStatusCommandHandler
    : ICommandHandler<SetBrandPlatformInvoiceStatusCommand, bool>
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "paid", "void" };

    private readonly ICoreDbContext _db;
    public SetBrandPlatformInvoiceStatusCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SetBrandPlatformInvoiceStatusCommand cmd, CancellationToken ct)
    {
        var status = cmd.Request.Status?.Trim().ToLowerInvariant() ?? "";
        if (!Allowed.Contains(status))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["status"] = ["Status must be 'paid' or 'void'."] });

        var inv = await _db.BrandPlatformInvoices.FirstOrDefaultAsync(i => i.Id == cmd.InvoiceId, ct);
        if (inv is null) return false;
        if (string.Equals(inv.Status, "void", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("A void invoice cannot change status.");
        if (string.Equals(inv.Status, status, StringComparison.OrdinalIgnoreCase)) return true; // idempotent

        inv.Status = status;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
