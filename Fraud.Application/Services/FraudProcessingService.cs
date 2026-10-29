using Fraud.Application.Data;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

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
        TransactionIngested message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.TransactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "TransactionId must be populated.",
                nameof(message));
        }

        var transaction = await _db.Transactions
            .SingleOrDefaultAsync(
                t => t.Id == message.TransactionId,
                cancellationToken);

        if (transaction is null)
        {
            throw new InvalidOperationException(
                $"Transaction {message.TransactionId} was not found.");
        }

        // Prevent an inconsistent event from reusing an existing ID.
        if (transaction.AccountId != message.AccountId
            || transaction.Amount != message.Amount
            || !string.Equals(
                transaction.Merchant,
                message.Merchant,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Transaction event payload does not match persisted transaction {message.TransactionId}.");
        }

        // A successfully completed transaction must not be evaluated again.
        if (transaction.ProcessingStatus
            == TransactionProcessingStatus.Completed)
        {
            _logger.LogInformation(
                "Skipping completed transaction {TransactionId}. IsFraud: {IsFraud}",
                message.TransactionId,
                transaction.IsFraud);

            return;
        }

        // Recover an alert created by the earlier implementation if its
        // processing outcome has not yet been recorded.
        var existingAlert = await _db.Alerts
            .SingleOrDefaultAsync(
                a => a.TransactionId == message.TransactionId,
                cancellationToken);

        if (existingAlert is not null)
        {
            transaction.ProcessingStatus =
                TransactionProcessingStatus.Completed;

            transaction.IsFraud = true;
            transaction.RiskScore = existingAlert.RiskScore;
            transaction.FraudReasons = existingAlert.Reason;
            transaction.ProcessedAt = existingAlert.CreatedAt;

            await SaveCompletionAsync(
                transaction,
                message.TransactionId,
                cancellationToken);

            _logger.LogInformation(
                "Recovered processing outcome from existing alert for {TransactionId}",
                message.TransactionId);

            return;
        }

        _logger.LogInformation(
            "Starting fraud evaluation for transaction {TransactionId}",
            message.TransactionId);

        var result = await _ruleEngine.EvaluateAsync(
            message,
            cancellationToken);

        var processedAt = DateTime.UtcNow;
        var reasons = string.Join(", ", result.reasons);

        transaction.ProcessingStatus =
            TransactionProcessingStatus.Completed;

        transaction.IsFraud = result.isFraud;
        transaction.RiskScore = result.score;
        transaction.FraudReasons = reasons;
        transaction.ProcessedAt = processedAt;

        if (result.isFraud)
        {
            _db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                TransactionId = message.TransactionId,
                RiskScore = result.score,
                Reason = reasons,
                CreatedAt = processedAt
            });
        }

        // Save the durable outcome and optional alert together.
        await SaveCompletionAsync(
            transaction,
            message.TransactionId,
            cancellationToken);

        _logger.LogInformation(
            "Fraud evaluation completed for {TransactionId}. IsFraud: {IsFraud}, RiskScore: {RiskScore}",
            message.TransactionId,
            result.isFraud,
            result.score);
    }

    private async Task SaveCompletionAsync(
    Transaction transaction,
    Guid transactionId,
    CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker may have completed this transaction first.
            if (await TryRecoverConcurrentCompletionAsync(
                    transaction,
                    transactionId,
                    cancellationToken))
            {
                return;
            }

            // The transaction is not completed in the database.
            // Preserve the failure so MassTransit can handle it.
            throw;
        }
        catch (DbUpdateException ex)
            when (IsAlertTransactionUniqueViolation(ex))
        {
            // Two workers may both have evaluated the same transaction
            // and attempted to insert its alert.
            //
            // SQL Server's unique index allows only one alert per transaction.
            // Treat the losing worker as a successful duplicate only if the
            // winning worker has already persisted a completed outcome.
            if (await TryRecoverConcurrentCompletionAsync(
                    transaction,
                    transactionId,
                    cancellationToken))
            {
                return;
            }

            // Do not swallow the exception if another worker's completion
            // cannot be confirmed.
            throw;
        }
    }

    private async Task<bool> TryRecoverConcurrentCompletionAsync(
        Transaction transaction,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        // Remove any alert insert left in the Added state after the failed save.
        // This prevents the same DbContext from retrying that stale insert.
        var pendingAlerts = _db.ChangeTracker
            .Entries<Alert>()
            .Where(entry =>
                entry.State == EntityState.Added
                && entry.Entity.TransactionId == transactionId)
            .ToList();

        foreach (var entry in pendingAlerts)
        {
            entry.State = EntityState.Detached;
        }

        // Refresh the tracked transaction with the authoritative database state.
        // ReloadAsync also refreshes its rowversion and resets its tracking state.
        await _db.Entry(transaction)
            .ReloadAsync(cancellationToken);

        if (transaction.ProcessingStatus
            != TransactionProcessingStatus.Completed)
        {
            return false;
        }

        _logger.LogInformation(
            "Transaction {TransactionId} was completed by another worker. " +
            "Treating concurrent processing as duplicate success.",
            transactionId);

        return true;
    }

    private static bool IsAlertTransactionUniqueViolation(
        DbUpdateException exception)
    {
        // SQL Server error 2601: duplicate key in a unique index.
        // SQL Server error 2627: violation of a unique constraint.
        //
        // Inspect the inner-exception chain and require the expected index
        // name so unrelated database errors are not treated as duplicates.
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Number is 2601 or 2627
                && sqlException.Message.Contains(
                    "IX_Alerts_TransactionId",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}