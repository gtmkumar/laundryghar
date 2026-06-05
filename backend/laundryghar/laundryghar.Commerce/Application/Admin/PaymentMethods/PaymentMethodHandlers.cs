using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.PaymentMethods;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPaymentMethodsQuery(int Page, int PageSize) : IRequest<PaginatedList<PaymentMethodDto>>;

public sealed class GetPaymentMethodsHandler : IRequestHandler<GetPaymentMethodsQuery, PaginatedList<PaymentMethodDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPaymentMethodsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PaymentMethodDto>> Handle(GetPaymentMethodsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.PaymentMethods
            .Where(x => x.BrandId == brandId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => ToDto(x));
        return PaginatedList<PaymentMethodDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static PaymentMethodDto ToDto(PaymentMethod x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.NameLocalized, x.MethodType,
        x.Gateway, x.IconUrl, x.MinimumAmount, x.MaximumAmount,
        x.ConvenienceFeeType, x.ConvenienceFeeValue, x.IsOnline, x.IsRefundable,
        x.IsActive, x.DisplayOrder, x.Status, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetPaymentMethodByIdQuery(Guid Id) : IRequest<PaymentMethodDto?>;

public sealed class GetPaymentMethodByIdHandler : IRequestHandler<GetPaymentMethodByIdQuery, PaymentMethodDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPaymentMethodByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto?> Handle(GetPaymentMethodByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetPaymentMethodsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreatePaymentMethodCommand(CreatePaymentMethodRequest Request, Guid? ActorId) : IRequest<PaymentMethodDto>;

public sealed class CreatePaymentMethodHandler : IRequestHandler<CreatePaymentMethodCommand, PaymentMethodDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePaymentMethodHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto> Handle(CreatePaymentMethodCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new PaymentMethod
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            Code                = req.Code,
            Name                = req.Name,
            NameLocalized       = req.NameLocalized,
            MethodType          = req.MethodType,
            Gateway             = req.Gateway,
            IconUrl             = req.IconUrl,
            MinimumAmount       = req.MinimumAmount,
            MaximumAmount       = req.MaximumAmount,
            ConvenienceFeeType  = req.ConvenienceFeeType,
            ConvenienceFeeValue = req.ConvenienceFeeValue,
            IsOnline            = req.IsOnline,
            IsRefundable        = req.IsRefundable,
            IsActive            = req.IsActive,
            DisplayOrder        = req.DisplayOrder,
            Config              = "{}",
            Status              = "active",
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId
        };

        _db.PaymentMethods.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetPaymentMethodsHandler.ToDto(entity);
    }
}

public sealed class CreatePaymentMethodValidator : AbstractValidator<CreatePaymentMethodCommand>
{
    public CreatePaymentMethodValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.NameLocalized).NotEmpty();
        RuleFor(x => x.Request.MethodType).NotEmpty().MaximumLength(50);
    }
}

public sealed record UpdatePaymentMethodCommand(Guid Id, UpdatePaymentMethodRequest Request, Guid? ActorId) : IRequest<PaymentMethodDto?>;

public sealed class UpdatePaymentMethodHandler : IRequestHandler<UpdatePaymentMethodCommand, PaymentMethodDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePaymentMethodHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto?> Handle(UpdatePaymentMethodCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                = req.Name;
        entity.NameLocalized       = req.NameLocalized;
        entity.Gateway             = req.Gateway;
        entity.IconUrl             = req.IconUrl;
        entity.MinimumAmount       = req.MinimumAmount;
        entity.MaximumAmount       = req.MaximumAmount;
        entity.ConvenienceFeeType  = req.ConvenienceFeeType;
        entity.ConvenienceFeeValue = req.ConvenienceFeeValue;
        entity.IsOnline            = req.IsOnline;
        entity.IsRefundable        = req.IsRefundable;
        entity.IsActive            = req.IsActive;
        entity.DisplayOrder        = req.DisplayOrder;
        entity.Status              = req.Status;
        entity.UpdatedAt           = DateTimeOffset.UtcNow;
        entity.UpdatedBy           = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return GetPaymentMethodsHandler.ToDto(entity);
    }
}

public sealed record DeletePaymentMethodCommand(Guid Id) : IRequest<bool>;

public sealed class DeletePaymentMethodHandler : IRequestHandler<DeletePaymentMethodCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePaymentMethodHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeletePaymentMethodCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        _db.PaymentMethods.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
