//1. What are we testing?

// The test deliberately injects a one-time failure when the worker saves a completed fraud outcome.

// Expected behavior:

// The worker receives a transaction event.
// Redis updates account, device and IP counters.
// The SQL save fails.
// The error reaches MassTransit's retry middleware.
// The transaction is processed again.
// Redis deduplication prevents the counters from incrementing twice.
// SQL successfully persists the completed outcome and alert.

using Fraud.Application.Configuration;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Application.Services;
using Fraud.Contracts;
using Fraud.Domain;
using Fraud.Worker;
using Fraud.Worker.Rules;
using Fraud.Worker.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace Fraud.Tests.Integration;

public sealed class SqlFailureAfterRedisRecoveryTests
{
    [Fact]
    public async Task SqlFailureAfterRedisSuccess_RetryDoesNotDoubleCountCounters()
    {
        var sqlConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(sqlConnectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test connection string.");

        var redisConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_REDIS")
            ?? "localhost:6379";

        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var deviceId = $"sql-retry-device-{Guid.NewGuid():N}";
        var ipAddress = $"192.0.2.{Random.Shared.Next(1, 255)}";
        var messageId = Guid.NewGuid();

        // Use a unique queue so this test does not compete with the
        // normal Fraud.Worker instance consuming transaction-queue.
        var queueName = $"fraud-sql-retry-{Guid.NewGuid():N}";

        var timestamp = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = transactionId,
            AccountId = accountId,
            Amount = 6000m,
            Merchant = "SqlRetryIntegrationTest",
            CreatedAt = timestamp,
            ProcessingStatus = TransactionProcessingStatus.Pending
        };

        var message = new TransactionIngested
        {
            TransactionId = transactionId,
            AccountId = accountId,
            Amount = transaction.Amount,
            Merchant = transaction.Merchant,
            Currency = "INR",
            Timestamp = timestamp,
            DeviceId = deviceId,
            IpAddress = ipAddress,
            Country = "IN",
            CorrelationId = Guid.NewGuid()
        };

        var counterKeys = new[]
        {
            $"velocity:account:{accountId}",
            $"velocity:device:{deviceId}",
            $"velocity:ip:{ipAddress}"
        };

        var deduplicationKeys = new[]
        {
            $"velocity:dedupe:account:{accountId}:{transactionId}",
            $"velocity:dedupe:device:{deviceId}:{transactionId}",
            $"velocity:dedupe:ip:{ipAddress}:{transactionId}"
        };

        var failureInterceptor =
            new FailOnceOutcomeSaveInterceptor(transactionId);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = sqlConnectionString,
                ["ConnectionStrings:Redis"] = redisConnectionString,
                ["Redis:ConnectionString"] = redisConnectionString
            })
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddConsole();
        });

        services.AddSingleton(failureInterceptor);

        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            options
                .UseSqlServer(sqlConnectionString)
                .AddInterceptors(
                    provider.GetRequiredService<FailOnceOutcomeSaveInterceptor>());
        });

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));

        // Keep the real Redis implementation. The test injects failure
        // only at the SQL save boundary.
        services.AddSingleton<RedisService>();
        services.AddSingleton<IRedisService>(
            provider => provider.GetRequiredService<RedisService>());

        services.Configure<VelocityOptions>(options =>
        {
            options.WindowSeconds = 60;
            options.AccountThreshold = 100;
            options.DeviceThreshold = 100;
            options.IpThreshold = 100;
            options.DeduplicationRetentionSeconds = 604800;
        });

        services.AddScoped<IFraudRule, AlwaysFraudRule>();
        services.AddScoped<IFraudRule, VelocityRule>();
        services.AddScoped<RuleEngine>();

        services.AddScoped<
            IFraudProcessingService,
            FraudProcessingService>();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<TransactionConsumer>();

            // Enable EF Core Consumer Outbox/Inbox against the same database
            // used for the business transaction and alert.
            x.AddEntityFrameworkOutbox<ApplicationDbContext>(options =>
            {
                options.UseSqlServer();
            });

            x.UsingRabbitMq((context, busCfg) =>
            {
                busCfg.Host("localhost", "/", host =>
                {
                    host.Username("guest");
                    host.Password("guest");
                });

                busCfg.ReceiveEndpoint(queueName, endpoint =>
                {
                    endpoint.Durable = true;
                    endpoint.AutoDelete = false;

                    // A short retry interval keeps the integration test quick.
                    endpoint.UseMessageRetry(retry =>
                    {
                        retry.Intervals(
                            TimeSpan.FromMilliseconds(250),
                            TimeSpan.FromMilliseconds(500),
                            TimeSpan.FromMilliseconds(750));
                    });

                    endpoint.UseEntityFrameworkOutbox<ApplicationDbContext>(
                        context);

                    endpoint.ConfigureConsumer<TransactionConsumer>(context);
                });
            });
        });

        await using var provider = services.BuildServiceProvider();

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(sqlConnectionString)
            .Options;

        // Seed with a separate context without the test interceptor.
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            seedDb.Transactions.Add(transaction);
            await seedDb.SaveChangesAsync();
        }

        var host = provider.GetRequiredService<IBusControl>();
        var busStarted = false;

        try
        {
            await host.StartAsync();
            busStarted = true;

            var redis = provider.GetRequiredService<IConnectionMultiplexer>();
            var redisDb = redis.GetDatabase();

            var endpoint = await host.GetSendEndpoint(
                new Uri($"queue:{queueName}"));

            // Publish one message with a stable MessageId.
            await endpoint.Send(message, sendContext =>
            {
                sendContext.MessageId = messageId;
            });

            // Wait for the worker to persist the eventual successful outcome.
            await WaitForCompletedAsync(
                dbOptions,
                transactionId,
                TimeSpan.FromSeconds(45));

            // The save interceptor must have injected a failure.
            Assert.True(
                failureInterceptor.FailureTriggered,
                "The test did not inject the intended SQL-save failure.");

            // The rule engine must have run again after the failed attempt.
            Assert.True(
                AlwaysFraudRule.EvaluationCount >= 2,
                $"Expected at least two rule evaluations, but found " +
                $"{AlwaysFraudRule.EvaluationCount}.");

            // Read counters after processing and retry have finished.
            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, counterKeys[0]));

            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, counterKeys[1]));

            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, counterKeys[2]));

            // Verify the durable result using a fresh context.
            await using var verifyDb =
                new ApplicationDbContext(dbOptions);

            var persistedTransaction = await verifyDb.Transactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId);

            var alerts = await verifyDb.Alerts
                .AsNoTracking()
                .Where(a => a.TransactionId == transactionId)
                .ToListAsync();

            Assert.Equal(
                TransactionProcessingStatus.Completed,
                persistedTransaction.ProcessingStatus);

            Assert.True(persistedTransaction.IsFraud);
            Assert.NotNull(persistedTransaction.ProcessedAt);
            Assert.Single(alerts);
        }
        finally
        {
            if (busStarted)
            {
                await host.StopAsync();
            }

            var redis = provider.GetRequiredService<IConnectionMultiplexer>();
            var redisDb = redis.GetDatabase();

            foreach (var key in counterKeys.Concat(deduplicationKeys))
            {
                await redisDb.KeyDeleteAsync(key);
            }

            await using var cleanupDb =
                new ApplicationDbContext(dbOptions);

            var alerts = await cleanupDb.Alerts
                .Where(a => a.TransactionId == transactionId)
                .ToListAsync();

            cleanupDb.Alerts.RemoveRange(alerts);

            var persistedTransaction = await cleanupDb.Transactions
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (persistedTransaction is not null)
            {
                cleanupDb.Transactions.Remove(persistedTransaction);
            }

            await cleanupDb.SaveChangesAsync();
        }
    }

    private static async Task<long> ReadCounterAsync(
        IDatabase database,
        string key)
    {
        var value = await database.StringGetAsync(key);

        if (!value.HasValue)
        {
            return 0;
        }

        return (long)value;
    }

    private static async Task WaitForCompletedAsync(
        DbContextOptions<ApplicationDbContext> options,
        Guid transactionId,
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            await using var db = new ApplicationDbContext(options);

            var status = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Id == transactionId)
                .Select(t => t.ProcessingStatus)
                .SingleAsync();

            if (status == TransactionProcessingStatus.Completed)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Transaction {transactionId} did not reach Completed " +
            $"within {timeout}.");
    }

    private sealed class AlwaysFraudRule : IFraudRule
    {
        private static int _evaluationCount;

        public static int EvaluationCount =>
            Volatile.Read(ref _evaluationCount);

        public string Name => "SQL Retry Integration Test";

        public double RiskScore => 0.95;

        public Task<bool> IsMatchAsync(
            TransactionIngested transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Interlocked.Increment(ref _evaluationCount);

            return Task.FromResult(true);
        }
    }

    private sealed class FailOnceOutcomeSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Guid _transactionId;
        private int _failureConsumed;
        private int _failureTriggered;

        public FailOnceOutcomeSaveInterceptor(Guid transactionId)
        {
            _transactionId = transactionId;
        }

        public bool FailureTriggered =>
            Volatile.Read(ref _failureTriggered) == 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext db
                && IsTargetOutcomeSave(db))
            {
                // Fail exactly once, when the worker attempts to save
                // the completed outcome and its alert after Redis evaluation.
                if (Interlocked.CompareExchange(
                        ref _failureConsumed,
                        1,
                        0) == 0)
                {
                    Interlocked.Exchange(ref _failureTriggered, 1);

                    return ValueTask.FromException<InterceptionResult<int>>(
                        new DbUpdateException(
                            "Simulated transient SQL save failure after Redis succeeded."));
                }
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        private bool IsTargetOutcomeSave(ApplicationDbContext db)
        {
            var transactionBeingCompleted = db.ChangeTracker
                .Entries<Transaction>()
                .Any(entry =>
                    entry.Entity.Id == _transactionId
                    && entry.Entity.ProcessingStatus
                        == TransactionProcessingStatus.Completed);

            var alertBeingAdded = db.ChangeTracker
                .Entries<Alert>()
                .Any(entry =>
                    entry.Entity.TransactionId == _transactionId
                    && entry.State == EntityState.Added);

            return transactionBeingCompleted && alertBeingAdded;
        }
    }
}

//Expected result
// Check	Expected result
// Failure interceptor	Triggered once
// Rule evaluation	At least twice
// Account counter	1
// Device counter	1
// IP counter	1
// Final transaction	Completed
// Fraud outcome	true
// Alerts	Exactly one

// If the test passes, we'll have verified that a failed save can be retried through the configured MassTransit pipeline while Redis deduplication prevents duplicate velocity counts.

// If it fails to compile or times out, share the exact error output. We should correct the test against your actual local interfaces and middleware configuration rather than replacing working production code.