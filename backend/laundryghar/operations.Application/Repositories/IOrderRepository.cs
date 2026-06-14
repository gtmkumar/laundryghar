using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Results;

namespace operations.Application.Repositories;

/// <summary>
/// Repository abstraction for <see cref="Order"/> (owned by the Application layer;
/// implemented in operations.Infrastructure).
/// </summary>
public interface IOrderRepository
{
    Task<Result<Order>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<Order>> GetByOrderNumberAsync(Guid brandId, string orderNumber, CancellationToken cancellationToken = default);
    Task<Result<Order>> AddAsync(Order order, CancellationToken cancellationToken = default);
}
