using laundryghar.Catalog.Application.Customer.Admin.Dtos;
using laundryghar.Catalog.Application.Customer.Admin.Queries;
using MediatR;

namespace laundryghar.Catalog.Application.Customer.Admin.Commands;

public sealed record AdminUpdateCustomerCommand(
    Guid Id,
    AdminUpdateCustomerRequest Request,
    Guid? ActorId
) : IRequest<AdminCustomerDto?>;

public sealed class AdminUpdateCustomerHandler : IRequestHandler<AdminUpdateCustomerCommand, AdminCustomerDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminUpdateCustomerHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto?> Handle(AdminUpdateCustomerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.FirstName       = req.FirstName ?? e.FirstName;
        e.LastName        = req.LastName  ?? e.LastName;
        e.Email           = req.Email     ?? e.Email;
        e.Gender          = req.Gender    ?? e.Gender;
        e.DateOfBirth     = req.DateOfBirth ?? e.DateOfBirth;
        e.CustomerSegment = req.CustomerSegment ?? e.CustomerSegment;
        e.RiskFlag        = req.RiskFlag ?? e.RiskFlag;
        e.UpdatedAt       = DateTimeOffset.UtcNow;
        e.UpdatedBy       = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return GetCustomersHandler.ToDto(e);
    }
}

public sealed record AdminDeleteCustomerCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class AdminDeleteCustomerHandler : IRequestHandler<AdminDeleteCustomerCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminDeleteCustomerHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(AdminDeleteCustomerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
