using Fraud.Application.Data;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fraud.Application.Services;

public class FraudProcessingService : IFraudProcessingService
{
    private readonly ApplicationDbContext _db;
    private readonly RuleEngine _ruleEngine;
    private readonly ILogger<FraudProcessingService> _logger;

    public FraudProcessingService(
        ApplicationDbContext db,
        RuleEngine ruleEngine,
        ILogger<FraudProcessingService> logger)
    {
        _db = db;
        _ruleEngine = ruleEngine;
        _logger = logger;
    }

    public async Task ProcessAsync(
        TransactionIngested transaction,
        CancellationToken cancellationToken = default)
    {
        // ---------------------------------------------
        // Idempotency
        // ---------------------------------------------

        var exists = await _db.Alerts
            .AnyAsync(
                a => a.TransactionId == transaction.TransactionId,
                cancellationToken);

        if (exists)
        {
            _logger.LogWarning(
                "Duplicate transaction ignored. " +
                "TransactionId: {TransactionId}",
                transaction.TransactionId);

            return;
        }

        // ---------------------------------------------
        // Fraud rule evaluation
        // ---------------------------------------------

        _logger.LogInformation(
            "Starting fraud rule evaluation. " +
            "TransactionId: {TransactionId}",
            transaction.TransactionId);

        var result = _ruleEngine.Evaluate(transaction);

        _logger.LogInformation(
            "Fraud rule evaluation completed. " +
            "TransactionId: {TransactionId}, " +
            "IsFraud: {IsFraud}, RiskScore: {RiskScore}",
            transaction.TransactionId,
            result.isFraud,
            result.score);

        // ---------------------------------------------
        // No fraud
        // ---------------------------------------------

        if (!result.isFraud)
        {
            _logger.LogInformation(
                "Transaction passed fraud evaluation. " +
                "TransactionId: {TransactionId}",
                transaction.TransactionId);

            return;
        }

        // ---------------------------------------------
        // Fraud detected
        // ---------------------------------------------

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.TransactionId,
            RiskScore = result.score,
            Reason = string.Join(
                ", ",
                result.reasons),
            CreatedAt = DateTime.UtcNow
        };

        _db.Alerts.Add(alert);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Fraud alert created. " +
            "TransactionId: {TransactionId}, " +
            "RiskScore: {RiskScore}, " +
            "Reason: {Reason}",
            transaction.TransactionId,
            result.score,
            alert.Reason);
    }
}