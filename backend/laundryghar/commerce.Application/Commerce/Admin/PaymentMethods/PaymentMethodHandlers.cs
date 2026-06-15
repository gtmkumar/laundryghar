using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Admin.PaymentMethods;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPaymentMethodsQuery(int Page, int PageSize) : IQuery<PaginatedList<PaymentMethodDto>>;

public sealed class GetPaymentMethodsHandler : IQueryHandler<GetPaymentMethodsQuery, PaginatedList<PaymentMethodDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetPaymentMethodsHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PaymentMethodDto>> HandleAsync(GetPaymentMethodsQuery q, CancellationToken ct)
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

public sealed record GetPaymentMethodByIdQuery(Guid Id) : IQuery<PaymentMethodDto?>;

public sealed class GetPaymentMethodByIdHandler : IQueryHandler<GetPaymentMethodByIdQuery, PaymentMethodDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetPaymentMethodByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto?> HandleAsync(GetPaymentMethodByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetPaymentMethodsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreatePaymentMethodCommand(CreatePaymentMethodRequest Request, Guid? ActorId) : ICommand<PaymentMethodDto>;

public sealed class CreatePaymentMethodHandler : ICommandHandler<CreatePaymentMethodCommand, PaymentMethodDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePaymentMethodHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto> HandleAsync(CreatePaymentMethodCommand cmd, CancellationToken ct)
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

public sealed class CreatePaymentMethodValidator : AbstractValidator<CreatePaymentMethodRequest>
{
    public CreatePaymentMethodValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameLocalized).NotEmpty().MustBeJsonObject();
        RuleFor(x => x.MethodType).NotEmpty().MaximumLength(50);
    }
}

public sealed record UpdatePaymentMethodCommand(Guid Id, UpdatePaymentMethodRequest Request, Guid? ActorId) : ICommand<PaymentMethodDto?>;

public sealed class UpdatePaymentMethodHandler : ICommandHandler<UpdatePaymentMethodCommand, PaymentMethodDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePaymentMethodHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaymentMethodDto?> HandleAsync(UpdatePaymentMethodCommand cmd, CancellationToken ct)
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

public sealed record DeletePaymentMethodCommand(Guid Id) : ICommand<bool>;

public sealed class DeletePaymentMethodHandler : ICommandHandler<DeletePaymentMethodCommand, bool>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePaymentMethodHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeletePaymentMethodCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        _db.PaymentMethods.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
