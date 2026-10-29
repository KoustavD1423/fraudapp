using Fraud.Application.Data;
using Fraud.Application.Rules;
using Fraud.Application.Services;
using Fraud.Contracts;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fraud.Tests.Integration;

public class ConcurrentFraudProcessingTests
{
    [Fact]
    public async Task ConcurrentProcessingOfSameTransaction_CreatesOnlyOneAlert()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test database connection string.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString!)
            .Options;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 6000m,
            Merchant = "ConcurrentProcessingTest",
            CreatedAt = DateTime.UtcNow,
            ProcessingStatus = TransactionProcessingStatus.Pending
        };

        var message = new TransactionIngested
        {
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId,
            Amount = transaction.Amount,
            Merchant = transaction.Merchant,
            Currency = "INR",
            Timestamp = transaction.CreatedAt,
            DeviceId = $"test-device-{Guid.NewGuid():N}",
            IpAddress = "203.0.113.10",
            CorrelationId = Guid.NewGuid()
        };

        var rule = new SynchronizingFraudRule();

        try
        {
            // Seed the transaction using SQL Server.
            await using (var setupContext =
                         new ApplicationDbContext(options))
            {
                setupContext.Transactions.Add(transaction);
                await setupContext.SaveChangesAsync();
            }

            // Start two independent workers concurrently.
            var workerA = ProcessWithNewContextAsync(
                options,
                message,
                rule);

            var workerB = ProcessWithNewContextAsync(
                options,
                message,
                rule);

            // The rule synchronizes both workers so they can both reach
            // evaluation before either worker saves its outcome.
            //
            // This deliberately creates the concurrency race.
            await Task.WhenAll(workerA, workerB);

            Assert.Equal(2, rule.CallCount);

            // Verify the durable result in a fresh context.
            await using var verificationContext =
                new ApplicationDbContext(options);

            var savedTransaction =
                await verificationContext.Transactions
                    .SingleAsync(t => t.Id == transaction.Id);

            Assert.Equal(
                TransactionProcessingStatus.Completed,
                savedTransaction.ProcessingStatus);

            Assert.Equal(true, savedTransaction.IsFraud);
            Assert.NotNull(savedTransaction.ProcessedAt);
            Assert.Equal(0.90, savedTransaction.RiskScore);

            var alerts = await verificationContext.Alerts
                .Where(a => a.TransactionId == transaction.Id)
                .ToListAsync();

            // Both workers evaluated the transaction, but only one alert
            // should have been committed.
            Assert.Single(alerts);

            Assert.Equal(0.90, alerts[0].RiskScore);

            Assert.Contains(
                "Concurrent Test Rule",
                alerts[0].Reason);
        }
        finally
        {
            // Remove only the rows created by this test.
            await using var cleanupContext =
                new ApplicationDbContext(options);

            var alertsToDelete = await cleanupContext.Alerts
                .Where(a => a.TransactionId == transaction.Id)
                .ToListAsync();

            cleanupContext.Alerts.RemoveRange(alertsToDelete);

            var transactionToDelete =
                await cleanupContext.Transactions
                    .SingleOrDefaultAsync(t => t.Id == transaction.Id);

            if (transactionToDelete is not null)
            {
                cleanupContext.Transactions.Remove(transactionToDelete);
            }

            await cleanupContext.SaveChangesAsync();
        }
    }

    private static async Task ProcessWithNewContextAsync(
        DbContextOptions<ApplicationDbContext> options,
        TransactionIngested message,
        IFraudRule rule)
    {
        await using var context = new ApplicationDbContext(options);

        var ruleEngine = new RuleEngine(new[] { rule });

        var service = new FraudProcessingService(
            context,
            ruleEngine,
            NullLogger<FraudProcessingService>.Instance);

        await service.ProcessAsync(message);
    }

    private sealed class SynchronizingFraudRule : IFraudRule
    {
        private readonly TaskCompletionSource<bool> _bothWorkersEvaluating =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public string Name => "Concurrent Test Rule";

        public double RiskScore => 0.90;

        public async Task<bool> IsMatchAsync(
            TransactionIngested tx,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentCount = Interlocked.Increment(ref _callCount);

            if (currentCount == 2)
            {
                _bothWorkersEvaluating.TrySetResult(true);
            }

            // Wait until both worker executions have reached the rule.
            await _bothWorkersEvaluating.Task.WaitAsync(
                TimeSpan.FromSeconds(20),
                cancellationToken);

            return true;
        }
    }
}