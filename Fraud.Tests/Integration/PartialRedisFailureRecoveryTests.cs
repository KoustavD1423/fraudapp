using Fraud.Application.Configuration;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Application.Services;
using Fraud.Contracts;
using Fraud.Domain;
using Fraud.Worker.Rules;
using Fraud.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Fraud.Tests.Integration;

public sealed class PartialRedisFailureRecoveryTests
{
    private readonly ITestOutputHelper _output;

    public PartialRedisFailureRecoveryTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public async Task PartialRedisFailure_RetryDoesNotDoubleCountSuccessfulDimensions()
    {
        var sqlConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(sqlConnectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test connection string.");

        var redisConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_REDIS")
            ?? "localhost:6379";

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(sqlConnectionString)
            .Options;

        // Unique values isolate this test from existing Redis activity.
        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var deviceId = $"partial-failure-device-{Guid.NewGuid():N}";

        // Use a valid, unique documentation-range test address.
        var ipAddress = $"192.0.2.{Random.Shared.Next(1, 255)}";
        var now = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = transactionId,
            AccountId = accountId,
            Amount = 6000m,
            Merchant = "PartialRedisFailureTest",
            CreatedAt = now,
            ProcessingStatus = TransactionProcessingStatus.Pending
        };

        var message = new TransactionIngested
        {
            TransactionId = transactionId,
            AccountId = accountId,
            Amount = 6000m,
            Merchant = "PartialRedisFailureTest",
            Currency = "INR",
            Timestamp = now,
            DeviceId = deviceId,
            IpAddress = ipAddress,
            Country = "IN",
            CorrelationId = Guid.NewGuid()
        };

        var accountCounterKey = $"velocity:account:{accountId}";
        var deviceCounterKey = $"velocity:device:{deviceId}";
        var ipCounterKey = $"velocity:ip:{ipAddress}";

        var counterKeys = new[]
        {
            accountCounterKey,
            deviceCounterKey,
            ipCounterKey
        };

        var deduplicationKeys = new[]
        {
            $"velocity:dedupe:account:{accountId}:{transactionId}",
            $"velocity:dedupe:device:{deviceId}:{transactionId}",
            $"velocity:dedupe:ip:{ipAddress}:{transactionId}"
        };

        var redisConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Redis"] = redisConnectionString,
                    ["Redis:ConnectionString"] = redisConnectionString
                })
            .Build();

        // Keep the Redis client alive for the entire test.
        await using var services = BuildServiceProvider(
            redisConfiguration,
            redisConnectionString);

        var redis = services.GetRequiredService<IRedisService>();
        var faultInjectingRedis = Assert.IsType<FailOnceRedisService>(redis);

        var velocityRule = services.GetRequiredService<VelocityRule>();

        var ruleEngine = new RuleEngine(
            new IFraudRule[]
            {
                new AlwaysFraudRule(),
                velocityRule
            });

        // A real Redis connection lets us verify the actual persisted counters.
        var multiplexer =
            services.GetRequiredService<IConnectionMultiplexer>();

        var redisDb = multiplexer.GetDatabase();

        try
        {
            // Arrange: create a pending transaction in SQL.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                db.Transactions.Add(transaction);
                await db.SaveChangesAsync();
            }

            // Attempt 1:
            // Account and IP updates should succeed.
            // The device operation fails before it calls the real Redis service.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                var processor = new FraudProcessingService(
                    db,
                    ruleEngine,
                    NullLogger<FraudProcessingService>.Instance);

                await Assert.ThrowsAnyAsync<Exception>(
                    () => processor.ProcessAsync(message));
            }

            Assert.True(
                faultInjectingRedis.DeviceFailureWasTriggered,
                "The test did not inject the intended device-counter failure.");

            // The failed evaluation must not persist a completed outcome.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                var persistedTransaction = await db.Transactions
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == transactionId);

                Assert.Equal(
                    TransactionProcessingStatus.Pending,
                    persistedTransaction.ProcessingStatus);

                Assert.Null(persistedTransaction.IsFraud);

                Assert.Equal(
                    0,
                    await db.Alerts.CountAsync(
                        a => a.TransactionId == transactionId));
            }

            // All successful Redis operations should have completed before
            // the first evaluation returned its exception.
            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, accountCounterKey));

            Assert.Equal(
                0L,
                await ReadCounterAsync(redisDb, deviceCounterKey));

            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, ipCounterKey));
            // Display counters after the first attempt.
            _output.WriteLine("========== AFTER FIRST ATTEMPT ==========");
            _output.WriteLine(
                $"Account counter: {await ReadCounterAsync(redisDb, accountCounterKey)}");
            _output.WriteLine(
                $"Device counter:  {await ReadCounterAsync(redisDb, deviceCounterKey)}");
            _output.WriteLine(
                $"IP counter:      {await ReadCounterAsync(redisDb, ipCounterKey)}");

            // Attempt 2:
            // The one-time device failure has been consumed.
            // All dimensions should now evaluate successfully.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                var processor = new FraudProcessingService(
                    db,
                    ruleEngine,
                    NullLogger<FraudProcessingService>.Instance);

                await processor.ProcessAsync(message);
            }

            // The account and IP operations must be deduplicated.
            // The device operation should now increment successfully once.
            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, accountCounterKey));

            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, deviceCounterKey));

            Assert.Equal(
                1L,
                await ReadCounterAsync(redisDb, ipCounterKey));

            // Display counters after the retry.
            _output.WriteLine("========== AFTER RETRY ==========");
            _output.WriteLine(
                $"Account counter: {await ReadCounterAsync(redisDb, accountCounterKey)}");
            _output.WriteLine(
                $"Device counter:  {await ReadCounterAsync(redisDb, deviceCounterKey)}");
            _output.WriteLine(
                $"IP counter:      {await ReadCounterAsync(redisDb, ipCounterKey)}");

            // The successful retry must persist one completed fraud outcome.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                var persistedTransaction = await db.Transactions
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == transactionId);

                var alerts = await db.Alerts
                    .Where(a => a.TransactionId == transactionId)
                    .ToListAsync();

                Assert.Equal(
                    TransactionProcessingStatus.Completed,
                    persistedTransaction.ProcessingStatus);

                Assert.True(persistedTransaction.IsFraud);
                Assert.NotNull(persistedTransaction.ProcessedAt);
                Assert.Single(alerts);
            }
        }
        finally
        {
            // Delete only the Redis keys and SQL records created by this test.
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

    private static ServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        string redisConnectionString)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddLogging();

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));

        // Resolve the real Redis implementation through DI.
        services.AddSingleton<RedisService>();

        // Inject a one-time fault while preserving real Redis behavior
        // for every operation that is not deliberately failed.
        services.AddSingleton<IRedisService>(provider =>
            new FailOnceRedisService(
                provider.GetRequiredService<RedisService>()));

        services.Configure<RedisOptions>(
            options =>
            {
                options.ConnectionString = redisConnectionString;
            });

        services.Configure<VelocityOptions>(
            options =>
            {
                options.WindowSeconds = 60;
                options.AccountThreshold = 100;
                options.DeviceThreshold = 100;
                options.IpThreshold = 100;
                options.DeduplicationRetentionSeconds = 604800;
            });

        services.AddTransient<VelocityRule>();

        return services.BuildServiceProvider();
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

    private sealed class AlwaysFraudRule : IFraudRule
    {
        public string Name => "PartialRedisFailureTest";

        public double RiskScore => 0.95;

        public Task<bool> IsMatchAsync(
            TransactionIngested transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(true);
        }
    }

    private sealed class FailOnceRedisService : IRedisService
    {
        private readonly IRedisService _inner;
        private int _deviceFailureRemaining = 1;

        public FailOnceRedisService(IRedisService inner)
        {
            _inner = inner;
        }

        public bool DeviceFailureWasTriggered =>
            Volatile.Read(ref _deviceFailureRemaining) == 0;

        public Task<long> IncrementWithExpiryAsync(
            string key,
            TimeSpan expiry,
            CancellationToken cancellationToken = default)
        {
            return _inner.IncrementWithExpiryAsync(
                key,
                expiry,
                cancellationToken);
        }

        public Task<long> IncrementOnceWithExpiryAsync(
     string counterKey,
     string deduplicationKey,
     TimeSpan counterExpiry,
     TimeSpan deduplicationExpiry,
     CancellationToken cancellationToken = default)
        {
            if (counterKey.StartsWith(
                    "velocity:device:",
                    StringComparison.Ordinal)
                && Interlocked.Exchange(
                    ref _deviceFailureRemaining,
                    0) == 1)
            {
                // Return a faulted task instead of throwing synchronously.
                // This allows the caller to create all dimension tasks and then
                // await them together.
                return Task.FromException<long>(
                    new InvalidOperationException(
                        "Simulated transient Redis device-counter failure."));
            }

            return _inner.IncrementOnceWithExpiryAsync(
                counterKey,
                deduplicationKey,
                counterExpiry,
                deduplicationExpiry,
                cancellationToken);
        }
    }
}