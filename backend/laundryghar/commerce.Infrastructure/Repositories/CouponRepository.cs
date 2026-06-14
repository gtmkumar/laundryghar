using commerce.Application.Repositories;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Results;
using Microsoft.EntityFrameworkCore;

namespace commerce.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ICouponRepository"/> over the shared context.</summary>
public sealed class CouponRepository : ICouponRepository
{
    private readonly LaundryGharDbContext _context;

    public CouponRepository(LaundryGharDbContext context) => _context = context;

    public async Task<Result<Coupon>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await _context.Set<Coupon>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);
        return Wrap(coupon);
    }

    public async Task<Result<Coupon>> GetByCodeAsync(Guid brandId, string code, CancellationToken cancellationToken = default)
    {
        var coupon = await _context.Set<Coupon>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.BrandId == brandId && c.Code == code && c.DeletedAt == null, cancellationToken);
        return Wrap(coupon);
    }

    public async Task<Result<Coupon>> AddAsync(Coupon coupon, CancellationToken cancellationToken = default)
    {
        await _context.Set<Coupon>().AddAsync(coupon, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new Result<Coupon>(new ResultCode(ResultType.Success, 1, "Coupon created."), coupon);
    }

    private static Result<Coupon> Wrap(Coupon? coupon) => coupon is null
        ? new Result<Coupon>(new ResultCode(ResultType.Error, 0, "Coupon not found."))
        : new Result<Coupon>(new ResultCode(ResultType.Success), coupon);
}
