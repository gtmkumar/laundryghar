using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Results;
using Microsoft.EntityFrameworkCore;
using operations.Application.Repositories;

namespace operations.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IOrderRepository"/>. Orders use a composite PK
/// (Id, CreatedAt), so lookups query by Id rather than FindAsync.</summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly LaundryGharDbContext _context;

    public OrderRepository(LaundryGharDbContext context) => _context = context;

    public async Task<Result<Order>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _context.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt == null, cancellationToken);
        return Wrap(order);
    }

    public async Task<Result<Order>> GetByOrderNumberAsync(Guid brandId, string orderNumber, CancellationToken cancellationToken = default)
    {
        var order = await _context.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.BrandId == brandId && o.OrderNumber == orderNumber && o.DeletedAt == null, cancellationToken);
        return Wrap(order);
    }

    public async Task<Result<Order>> AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Set<Order>().AddAsync(order, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new Result<Order>(new ResultCode(ResultType.Success, 1, "Order created."), order);
    }

    private static Result<Order> Wrap(Order? order) => order is null
        ? new Result<Order>(new ResultCode(ResultType.Error, 0, "Order not found."))
        : new Result<Order>(new ResultCode(ResultType.Success), order);
}
