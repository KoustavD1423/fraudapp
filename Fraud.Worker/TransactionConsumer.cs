using MassTransit;
using Fraud.Contracts;
using Fraud.Infrastructure;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Fraud.Application.Data;
using Fraud.Application.Rules;

namespace Fraud.Worker;

public class TransactionConsumer : IConsumer<TransactionIngested>
{
    private readonly ApplicationDbContext _db;
    private readonly RuleEngine _ruleEngine;

    public TransactionConsumer(ApplicationDbContext db, RuleEngine ruleEngine)
    {
        _db = db;
        _ruleEngine = ruleEngine;
    }

    public async Task Consume(ConsumeContext<TransactionIngested> context)
    {
        var message = context.Message;
        var correlationId = context.CorrelationId ?? message.TransactionId;

        // Correlation logging
        if (context.CorrelationId == null)
        {
            Log.Warning(
                "Missing CorrelationId | MessageId: {MessageId} | Falling back to TransactionId: {TransactionId}",
                context.MessageId,
                correlationId);
        }
        else
        {
            Log.Information("CorrelationId: {CorrelationId}", correlationId);
        }

        // Idempotency check
        var exists = await _db.Alerts
            .AnyAsync(a => a.TransactionId == message.TransactionId, context.CancellationToken);

        if (exists)
        {
            Log.Warning("Duplicate message ignored for Transaction {TransactionId}", message.TransactionId);
            return;
        }

        Log.Information(
            "Processing Tx {TransactionId} | CorrelationId: {CorrelationId} | MessageId: {MessageId}",
            message.TransactionId,
            correlationId,
            context.MessageId);

        var result = _ruleEngine.Evaluate(message);

        // ✅ Proper if/else — no dangling else
        if (result.isFraud)
        {
            var alert = new Alert
            {
                TransactionId = message.TransactionId,
                RiskScore = result.score,
                Reason = string.Join(", ", result.reasons),
                CreatedAt = DateTime.UtcNow
            };

            _db.Alerts.Add(alert);
            await _db.SaveChangesAsync(context.CancellationToken);

            Log.Information("Fraud alert created for Transaction {TransactionId}", message.TransactionId);
        }
        else
        {
            Log.Information("Transaction {TransactionId} is safe", message.TransactionId);
        }
    }
}