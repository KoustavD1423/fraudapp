using Fraud.Application.Data;
using Fraud.Application.Rules;
using Fraud.Application.Services;
using Fraud.Contracts;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fraud.Tests.Unit;

public class FraudProcessingServiceTests
{
    [Fact]
    public async Task LegitimateTransactionDeliveredTwice_IsEvaluatedOnlyOnce()
    {
        var databaseName = Guid.NewGuid().ToString();
        var transaction = CreateTransaction();

        await SeedTransactionAsync(databaseName, transaction);

        var rule = new CountingRule(matches: false);
        var engine = new RuleEngine(new[] { rule });
        var message = CreateMessage(transaction);

        await ProcessAsync(databaseName, engine, message);
        await ProcessAsync(databaseName, engine, message);

        Assert.Equal(1, rule.CallCount);

        await using var db = CreateContext(databaseName);

        var saved = await db.Transactions
            .SingleAsync(t => t.Id == transaction.Id);

        Assert.Equal(
            TransactionProcessingStatus.Completed,
            saved.ProcessingStatus);

        Assert.Equal(false, saved.IsFraud);
        Assert.NotNull(saved.ProcessedAt);
        Assert.Empty(await db.Alerts
            .Where(a => a.TransactionId == transaction.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task FraudulentTransactionDeliveredTwice_CreatesOnlyOneAlert()
    {
        var databaseName = Guid.NewGuid().ToString();
        var transaction = CreateTransaction();

        await SeedTransactionAsync(databaseName, transaction);

        var rule = new CountingRule(matches: true);
        var engine = new RuleEngine(new[] { rule });
        var message = CreateMessage(transaction);

        await ProcessAsync(databaseName, engine, message);
        await ProcessAsync(databaseName, engine, message);

        Assert.Equal(1, rule.CallCount);

        await using var db = CreateContext(databaseName);

        var saved = await db.Transactions
            .SingleAsync(t => t.Id == transaction.Id);

        Assert.Equal(
            TransactionProcessingStatus.Completed,
            saved.ProcessingStatus);

        Assert.Equal(true, saved.IsFraud);
        Assert.Equal(0.90, saved.RiskScore);
        Assert.NotNull(saved.ProcessedAt);

        var alerts = await db.Alerts
            .Where(a => a.TransactionId == transaction.Id)
            .ToListAsync();

        Assert.Single(alerts);
        Assert.Equal(0.90, alerts[0].RiskScore);
        Assert.Contains("Test Rule", alerts[0].Reason);
    }

    [Fact]
    public async Task MissingTransaction_ThrowsAndDoesNotEvaluateRules()
    {
        var databaseName = Guid.NewGuid().ToString();

        var rule = new CountingRule(matches: true);
        var engine = new RuleEngine(new[] { rule });

        var message = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 100m,
            Merchant = "Missing Transaction",
            Currency = "INR",
            Timestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid()
        };

        await using var db = CreateContext(databaseName);

        var service = CreateService(db, engine);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(message));

        Assert.Equal(0, rule.CallCount);
    }

    [Fact]
    public async Task MismatchedPayload_ThrowsAndDoesNotEvaluateRules()
    {
        var databaseName = Guid.NewGuid().ToString();
        var transaction = CreateTransaction();

        await SeedTransactionAsync(databaseName, transaction);

        var rule = new CountingRule(matches: true);
        var engine = new RuleEngine(new[] { rule });

        var message = CreateMessage(transaction);
        message.Amount = transaction.Amount + 100m;

        await using var db = CreateContext(databaseName);

        var service = CreateService(db, engine);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(message));

        Assert.Equal(0, rule.CallCount);

        await using var verificationDb = CreateContext(databaseName);

        var saved = await verificationDb.Transactions
            .SingleAsync(t => t.Id == transaction.Id);

        Assert.Equal(
            TransactionProcessingStatus.Pending,
            saved.ProcessingStatus);
    }

    [Fact]
    public async Task ExistingAlertWithoutCompletedStatus_RecoversOutcome()
    {
        var databaseName = Guid.NewGuid().ToString();
        var transaction = CreateTransaction();

        await using (var seedDb = CreateContext(databaseName))
        {
            seedDb.Transactions.Add(transaction);

            seedDb.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                RiskScore = 0.90,
                Reason = "Previously detected fraud",
                CreatedAt = DateTime.UtcNow
            });

            await seedDb.SaveChangesAsync();
        }

        var rule = new CountingRule(matches: true);
        var engine = new RuleEngine(new[] { rule });

        await ProcessAsync(
            databaseName,
            engine,
            CreateMessage(transaction));

        Assert.Equal(0, rule.CallCount);

        await using var verificationDb = CreateContext(databaseName);

        var saved = await verificationDb.Transactions
            .SingleAsync(t => t.Id == transaction.Id);

        Assert.Equal(
            TransactionProcessingStatus.Completed,
            saved.ProcessingStatus);

        Assert.Equal(true, saved.IsFraud);
        Assert.Equal(0.90, saved.RiskScore);
        Assert.Contains("Previously detected fraud", saved.FraudReasons);

        Assert.Single(await verificationDb.Alerts
            .Where(a => a.TransactionId == transaction.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task DifferentTransactions_AreBothEvaluated()
    {
        var databaseName = Guid.NewGuid().ToString();

        var first = CreateTransaction();
        var second = CreateTransaction();

        await SeedTransactionAsync(databaseName, first);
        await SeedTransactionAsync(databaseName, second);

        var rule = new CountingRule(matches: false);
        var engine = new RuleEngine(new[] { rule });

        await ProcessAsync(
            databaseName,
            engine,
            CreateMessage(first));

        await ProcessAsync(
            databaseName,
            engine,
            CreateMessage(second));

        Assert.Equal(2, rule.CallCount);

        await using var db = CreateContext(databaseName);

        var completedCount = await db.Transactions.CountAsync(t =>
            (t.Id == first.Id || t.Id == second.Id) &&
            t.ProcessingStatus == TransactionProcessingStatus.Completed);

        Assert.Equal(2, completedCount);
    }

    private static Transaction CreateTransaction()
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 100m,
            Merchant = "Test Merchant",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static TransactionIngested CreateMessage(Transaction transaction)
    {
        return new TransactionIngested
        {
            TransactionId = transaction.Id,
            AccountId = transaction.AccountId,
            Amount = transaction.Amount,
            Merchant = transaction.Merchant,
            Currency = "INR",
            Timestamp = transaction.CreatedAt,
            CorrelationId = Guid.NewGuid()
        };
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedTransactionAsync(
        string databaseName,
        Transaction transaction)
    {
        await using var db = CreateContext(databaseName);

        db.Transactions.Add(transaction);

        await db.SaveChangesAsync();
    }

    private static async Task ProcessAsync(
        string databaseName,
        RuleEngine engine,
        TransactionIngested message)
    {
        await using var db = CreateContext(databaseName);

        var service = CreateService(db, engine);

        await service.ProcessAsync(message);
    }

    private static FraudProcessingService CreateService(
        ApplicationDbContext db,
        RuleEngine engine)
    {
        return new FraudProcessingService(
            db,
            engine,
            NullLogger<FraudProcessingService>.Instance);
    }

    private sealed class CountingRule : IFraudRule
    {
        private readonly bool _matches;

        public int CallCount { get; private set; }

        public string Name => "Test Rule";

        public double RiskScore => 0.90;

        public CountingRule(bool matches)
        {
            _matches = matches;
        }

        public Task<bool> IsMatchAsync(
            TransactionIngested tx,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            return Task.FromResult(_matches);
        }
    }
}