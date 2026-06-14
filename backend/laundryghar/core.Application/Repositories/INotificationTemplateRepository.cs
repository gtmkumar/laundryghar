using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Results;

namespace core.Application.Repositories;

/// <summary>
/// Repository abstraction for <see cref="NotificationTemplate"/> (owned by the Application layer;
/// implemented in core.Infrastructure).
/// </summary>
public interface INotificationTemplateRepository
{
    Task<Result<NotificationTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<NotificationTemplate>> GetByCodeAsync(Guid brandId, string code, CancellationToken cancellationToken = default);
    Task<Result<NotificationTemplate>> AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
}
