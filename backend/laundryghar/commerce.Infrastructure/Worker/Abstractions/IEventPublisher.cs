namespace commerce.Infrastructure.Worker.Abstractions;

/// <summary>Represents a single domain event to be published to the message broker.</summary>
public sealed record EventPublishRequest(
    Guid   EventId,
    string EventType,
    string AggregateType,
    Guid   AggregateId,
    string Payload,
    string? RoutingKey,
    string? TargetExchange);

/// <summary>
/// Abstraction over the message-broker transport (RabbitMQ/MassTransit in production).
/// The <see cref="commerce.Infrastructure.Worker.Stubs.LoggingEventPublisher"/> dev stub logs
/// the event; swap for a real MassTransit/IBus implementation without changing the relay loop.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(EventPublishRequest request, CancellationToken ct = default);
}
