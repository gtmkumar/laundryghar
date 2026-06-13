using laundryghar.Worker.Abstractions;

namespace laundryghar.Worker.Infrastructure.Stubs;

/// <summary>
/// Development stub: logs domain-event dispatch instead of publishing to RabbitMQ/MassTransit.
/// Replace with a real IBus/IPublishEndpoint implementation for production.
/// </summary>
internal sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
        => _logger = logger;

    public Task PublishAsync(EventPublishRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[EVENT] type={EventType} aggregate={AggregateType}:{AggregateId} " +
            "routingKey={RoutingKey} exchange={Exchange} eventId={EventId}",
            request.EventType,
            request.AggregateType,
            request.AggregateId,
            request.RoutingKey,
            request.TargetExchange,
            request.EventId);

        return Task.CompletedTask;
    }
}
