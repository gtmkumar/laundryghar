using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Orders.Commands;

/// <summary>
/// Customer rates a delivered order — idempotent upsert (re-rating updates the existing score).
/// Self-filter: customerId from JWT sub must match order.customer_id.
/// Only allowed for orders in 'delivered' or 'closed' status.
/// </summary>
public sealed record RateOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    RateOrderRequest Request) : ICommand<RateOrderResult>;

/// <summary>Union result so the endpoint can distinguish 404, 422, and 200.</summary>
public enum RateOrderResultKind { Ok, NotFound, InvalidStatus }

public sealed record RateOrderResult(RateOrderResultKind Kind, OrderDto? Order = null);

public sealed class RateOrderHandler : ICommandHandler<RateOrderCommand, RateOrderResult>
{
    private static readonly string[] RateableStatuses = ["delivered", "closed"];

    private readonly IOperationsDbContext _db;

    public RateOrderHandler(IOperationsDbContext db) => _db = db;

    public async Task<RateOrderResult> HandleAsync(RateOrderCommand cmd, CancellationToken ct)
    {
        // Self-filter: only the order owner may rate
        var order = await _db.Orders
            .FirstOrDefaultAsync(
                o => o.Id == cmd.OrderId && o.CustomerId == cmd.CustomerId, ct);

        if (order is null || order.DeletedAt != null)
            return new RateOrderResult(RateOrderResultKind.NotFound);

        if (!RateableStatuses.Contains(order.Status))
            return new RateOrderResult(RateOrderResultKind.InvalidStatus);

        var now = DateTimeOffset.UtcNow;
        order.Rating        = (short)cmd.Request.Score;
        order.RatingComment = cmd.Request.Comment;
        order.RatedAt       = now;
        order.UpdatedAt     = now;
        order.UpdatedBy     = cmd.CustomerId;
        order.Version++;

        await _db.SaveChangesAsync(ct);
        return new RateOrderResult(RateOrderResultKind.Ok, CreateOrderHandler.ToDto(order));
    }
}

public sealed class RateOrderValidator : AbstractValidator<RateOrderCommand>
{
    public RateOrderValidator()
    {
        RuleFor(x => x.Request.Score)
            .InclusiveBetween(1, 5)
            .WithMessage("Score must be between 1 and 5.");

        RuleFor(x => x.Request.Comment)
            .MaximumLength(500)
            .When(x => x.Request.Comment is not null)
            .WithMessage("Comment must not exceed 500 characters.");
    }
}
