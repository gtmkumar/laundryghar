using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.NotificationOutbox.Commands.RetryNotificationOutbox;

// Requeue a failed outbox entry for delivery. Only 'failed' entries are eligible.
public sealed record RetryNotificationOutboxCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class RetryNotificationOutboxCommandHandler : ICommandHandler<RetryNotificationOutboxCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public RetryNotificationOutboxCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(RetryNotificationOutboxCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.NotificationOutboxes
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.BrandId == brandId, cancellationToken);

        if (entity is null) return false;
        if (entity.Status != "failed")
            throw new BusinessRuleException($"Cannot retry outbox entry with status '{entity.Status}'. Only 'failed' entries can be retried.");

        entity.Status = "pending";
        entity.NextAttemptAt = DateTimeOffset.UtcNow;
        entity.LastError = null;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
