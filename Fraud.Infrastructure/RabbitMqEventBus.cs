using Fraud.Application.Interfaces;
using MassTransit;

namespace Fraud.Infrastructure;

public class RabbitMqEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public RabbitMqEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<T>(T message)
    {
        // 🔥 CRITICAL: Generate a CorrelationId for every published message
        await _publishEndpoint.Publish(message, context =>
        {
            context.CorrelationId = Guid.NewGuid();
        });
    }
}