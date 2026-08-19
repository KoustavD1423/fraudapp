using Fraud.Application.Services;
using Fraud.Contracts;
using MassTransit;
using LogContext = Serilog.Context.LogContext;

namespace Fraud.Worker;

public class TransactionConsumer : IConsumer<TransactionIngested>
{
    private readonly IFraudProcessingService _fraudProcessingService;
    private readonly ILogger<TransactionConsumer> _logger;

    public TransactionConsumer(
        IFraudProcessingService fraudProcessingService,
        ILogger<TransactionConsumer> logger)
    {
        _fraudProcessingService = fraudProcessingService;
        _logger = logger;
    }

    public async Task Consume(
        ConsumeContext<TransactionIngested> context)
    {
        var message = context.Message;

        // Prefer MassTransit's CorrelationId.
        // Fall back to the CorrelationId carried inside the event.
        var correlationId =
            context.CorrelationId
            ?? message.CorrelationId;

        using (LogContext.PushProperty(
                   "CorrelationId",
                   correlationId))
        using (LogContext.PushProperty(
                   "TransactionId",
                   message.TransactionId))
        using (LogContext.PushProperty(
                   "MessageId",
                   context.MessageId))
        {
            _logger.LogInformation(
                "TransactionIngested event received. " +
                "AccountId: {AccountId}, " +
                "Merchant: {Merchant}, " +
                "Amount: {Amount}, " +
                "Country: {Country}",
                message.AccountId,
                message.Merchant,
                message.Amount,
                message.Country);

            // ---------------------------------------------
            // Correlation validation
            // ---------------------------------------------

            if (context.CorrelationId == null)
            {
                _logger.LogWarning(
                    "MassTransit CorrelationId was missing. " +
                    "Using event CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // ---------------------------------------------
            // Delegate fraud processing
            // ---------------------------------------------

            await _fraudProcessingService.ProcessAsync(
                message,
                context.CancellationToken);
        }
    }
}