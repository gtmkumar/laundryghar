using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Results;

namespace commerce.Application.Repositories;

/// <summary>
/// Repository abstraction for <see cref="Coupon"/> (Dependency Inversion: the Application layer
/// owns this contract; commerce.Infrastructure provides the EF Core implementation).
/// </summary>
public interface ICouponRepository
{
    Task<Result<Coupon>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<Coupon>> GetByCodeAsync(Guid brandId, string code, CancellationToken cancellationToken = default);
    Task<Result<Coupon>> AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
}
