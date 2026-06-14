using core.Application.Repositories;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Results;
using Microsoft.EntityFrameworkCore;

namespace core.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="INotificationTemplateRepository"/>.</summary>
public sealed class NotificationTemplateRepository : INotificationTemplateRepository
{
    private readonly LaundryGharDbContext _context;

    public NotificationTemplateRepository(LaundryGharDbContext context) => _context = context;

    public async Task<Result<NotificationTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _context.Set<NotificationTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return Wrap(template);
    }

    public async Task<Result<NotificationTemplate>> GetByCodeAsync(Guid brandId, string code, CancellationToken cancellationToken = default)
    {
        var template = await _context.Set<NotificationTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.BrandId == brandId && t.Code == code, cancellationToken);
        return Wrap(template);
    }

    public async Task<Result<NotificationTemplate>> AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
    {
        await _context.Set<NotificationTemplate>().AddAsync(template, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new Result<NotificationTemplate>(new ResultCode(ResultType.Success, 1, "Template created."), template);
    }

    private static Result<NotificationTemplate> Wrap(NotificationTemplate? template) => template is null
        ? new Result<NotificationTemplate>(new ResultCode(ResultType.Error, 0, "Notification template not found."))
        : new Result<NotificationTemplate>(new ResultCode(ResultType.Success), template);
}
