using System.Data;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Fraud.Application.Data;
using Fraud.Contracts;
using Fraud.Domain;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using StackExchange.Redis;
using Fraud.Application.Configuration;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Application.Services;
using Fraud.Worker;
using Fraud.Worker.Rules;
using Fraud.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fraud.Tests.Integration;
// The first published message should be consumed and its transaction should become Completed. 
// Repeating that same message ID should be recognized by the Inbox. 
// The diagnostic message with the same ID but a different transaction should not process that second transaction.
public sealed class ConsumerInboxIntegrationTests
{
    [Fact]
    public async Task DuplicateMessageId_IsSuppressedByConsumerInbox()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test connection string.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var first = CreateTestCase(
            amount: 6000m,
            merchant: "InboxDedupFirst");

        var second = CreateTestCase(
            amount: 6000m,
            merchant: "InboxDedupSecond");

        var transactionIds = new[]
        {
            first.Transaction.Id,
            second.Transaction.Id
        };

        var messageId = Guid.NewGuid();

        // Seed both transactions in the same database used by the worker.
        await using (var db = new ApplicationDbContext(options))
        {
            db.Transactions.AddRange(
                first.Transaction,
                second.Transaction);

            await db.SaveChangesAsync();
        }

        var redisConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_REDIS")
            ?? "localhost:6379";

        var queueName = $"fraud-inbox-test-{Guid.NewGuid():N}";

        IHost? workerHost = null;

        try
        {
            workerHost = await StartTestWorkerAsync(
                connectionString!,
                redisConnectionString,
                queueName);

            var bus = workerHost.Services.GetRequiredService<IBus>();

            // Send directly to the test-owned queue. The temporary=true address
            // matches Durable=false and AutoDelete=true on the receive endpoint.
            // This avoids relying on a publish-exchange binding in an ephemeral test topology.
            var sendEndpoint = await bus.GetSendEndpoint(
                new Uri($"queue:{queueName}?temporary=true"));

            // Delivery 1: process the first transaction.
            await sendEndpoint.Send(first.Message, context =>
            {
                context.MessageId = messageId;
            });

            await WaitForProcessingStatusAsync(
                options,
                first.Transaction.Id,
                TransactionProcessingStatus.Completed,
                TimeSpan.FromSeconds(30));

            await WaitForInboxReceiveCountAsync(
                connectionString!,
                messageId,
                minimumCount: 1,
                TimeSpan.FromSeconds(15));

            // Delivery 2: replay the exact same message with the same ID.
            await sendEndpoint.Send(first.Message, context =>
            {
                context.MessageId = messageId;
            });

            await WaitForInboxReceiveCountAsync(
                connectionString!,
                messageId,
                minimumCount: 2,
                TimeSpan.FromSeconds(15));

            // Diagnostic delivery: reuse the ID for a different transaction.
            // A working Inbox should suppress this delivery too.
            await sendEndpoint.Send(second.Message, context =>
            {
                context.MessageId = messageId;
            });

            await WaitForInboxReceiveCountAsync(
                connectionString!,
                messageId,
                minimumCount: 3,
                TimeSpan.FromSeconds(15));

            // Read fresh database state; do not use the originally tracked entities.
            await using var verificationDb =
                new ApplicationDbContext(options);

            var firstTransaction = await verificationDb.Transactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == first.Transaction.Id);

            var secondTransaction = await verificationDb.Transactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == second.Transaction.Id);

            var firstAlertCount = await verificationDb.Alerts
                .CountAsync(a => a.TransactionId == first.Transaction.Id);

            var secondAlertCount = await verificationDb.Alerts
                .CountAsync(a => a.TransactionId == second.Transaction.Id);

            Assert.Equal(
                TransactionProcessingStatus.Completed,
                firstTransaction.ProcessingStatus);

            Assert.Equal(1, firstAlertCount);

            // The second transaction has a different TransactionId.
            // It must stay Pending because its message ID was already consumed.
            Assert.Equal(
                TransactionProcessingStatus.Pending,
                secondTransaction.ProcessingStatus);

            Assert.Equal(0, secondAlertCount);
        }
        finally
        {
            if (workerHost is not null)
            {
                await workerHost.StopAsync();
                workerHost.Dispose();
            }

            // Remove only the two transactions created by this test.
            await using var cleanupDb =
                new ApplicationDbContext(options);

            var alerts = await cleanupDb.Alerts
                .Where(a => transactionIds.Contains(a.TransactionId))
                .ToListAsync();

            cleanupDb.Alerts.RemoveRange(alerts);

            var transactions = await cleanupDb.Transactions
                .Where(t => transactionIds.Contains(t.Id))
                .ToListAsync();

            cleanupDb.Transactions.RemoveRange(transactions);

            await cleanupDb.SaveChangesAsync();
        }
    }

    //Consumer Inbox Dedupe
    // What this test above proves

    // The first transaction should become Completed and have one alert.

    // The repeated message should be recognized using the same MessageId. The diagnostic message then uses that same ID for a second transaction. 
    //If Inbox deduplication is working, the worker should not process the second transaction, and it should remain Pending.

    // The test checks the persisted InboxState.ReceiveCount as well as the business records. 
    //A missing Inbox record or a count that fails to increase will cause the test to fail, 
    //directing us to the Consumer Outbox configuration or message-delivery path.

    [Fact]
    public async Task DifferentMessageIdsForSameTransaction_DoNotRepeatBusinessEffects()
    {
        var sqlConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(sqlConnectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test connection string.");

        var redisConnectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_REDIS")
            ?? "localhost:6379";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(sqlConnectionString)
            .Options;

        var testCase = CreateTestCase(
            amount: 6000m,
            merchant: "BusinessIdempotencyTest");

        var transactionId = testCase.Transaction.Id;

        // Use unique Redis keys so the test does not interfere with other tests.
        testCase.Message.IpAddress =
            $"192.0.2.{Random.Shared.Next(1, 255)}";

        var accountCounterKey =
            $"velocity:account:{testCase.Message.AccountId}";

        var deviceCounterKey =
            $"velocity:device:{testCase.Message.DeviceId}";

        var ipCounterKey =
            $"velocity:ip:{testCase.Message.IpAddress}";

        var counterKeys = new[]
        {
        accountCounterKey,
        deviceCounterKey,
        ipCounterKey
    };

        var deduplicationKeys = new[]
        {
        $"velocity:dedupe:account:{testCase.Message.AccountId}:{transactionId}",
        $"velocity:dedupe:device:{testCase.Message.DeviceId}:{transactionId}",
        $"velocity:dedupe:ip:{testCase.Message.IpAddress}:{transactionId}"
    };

        using var redis =
            ConnectionMultiplexer.Connect(redisConnectionString);

        var redisDb = redis.GetDatabase();

        // This test owns its consumer host and queue.
        // It does not depend on the separately running Fraud.Worker process.
        var queueName = $"fraud-business-idempotency-{Guid.NewGuid():N}";

        IHost? workerHost = null;

        try
        {
            // Seed the transaction before publishing its event.
            await using (var db = new ApplicationDbContext(options))
            {
                db.Transactions.Add(testCase.Transaction);
                await db.SaveChangesAsync();
            }

            // Start an in-process consumer using the shared test helper.
            workerHost = await StartTestWorkerAsync(
                sqlConnectionString!,
                redisConnectionString,
                queueName);

            var bus = workerHost.Services.GetRequiredService<IBus>();
            var sendEndpoint = await bus.GetSendEndpoint(
                new Uri($"queue:{queueName}?temporary=true"));

            // --------------------------------------------------
            // First delivery: this transaction should be processed.
            // --------------------------------------------------

            var firstMessageId = Guid.NewGuid();

            await sendEndpoint.Send(testCase.Message, context =>
            {
                context.MessageId = firstMessageId;
            });

            await WaitForProcessingStatusAsync(
                options,
                transactionId,
                TransactionProcessingStatus.Completed,
                TimeSpan.FromSeconds(30));

            await WaitForInboxConsumedAsync(
                sqlConnectionString!,
                firstMessageId,
                TimeSpan.FromSeconds(15));

            // Record the completed outcome and Redis counts.
            DateTime? originalProcessedAt;

            await using (var db = new ApplicationDbContext(options))
            {
                var persistedTransaction = await db.Transactions
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == transactionId);

                Assert.Equal(
                    TransactionProcessingStatus.Completed,
                    persistedTransaction.ProcessingStatus);

                originalProcessedAt = persistedTransaction.ProcessedAt;
                Assert.NotNull(originalProcessedAt);

                Assert.Equal(
                    1,
                    await db.Alerts.CountAsync(
                        a => a.TransactionId == transactionId));
            }

            var accountCountBefore =
                await ReadCounterAsync(redisDb, accountCounterKey);

            var deviceCountBefore =
                await ReadCounterAsync(redisDb, deviceCounterKey);

            var ipCountBefore =
                await ReadCounterAsync(redisDb, ipCounterKey);

            Assert.True(accountCountBefore >= 1);
            Assert.True(deviceCountBefore >= 1);
            Assert.True(ipCountBefore >= 1);

            // --------------------------------------------------
            // Second delivery: same TransactionId, NEW MessageId.
            // --------------------------------------------------

            var secondMessageId = Guid.NewGuid();

            Assert.NotEqual(firstMessageId, secondMessageId);

            await sendEndpoint.Send(testCase.Message, context =>
            {
                context.MessageId = secondMessageId;
            });

            // The Inbox should recognize this as a new message ID.
            // Business idempotency must prevent the work from being repeated.
            await WaitForInboxConsumedAsync(
                sqlConnectionString!,
                secondMessageId,
                TimeSpan.FromSeconds(15));

            // --------------------------------------------------
            // Verify durable business effects.
            // --------------------------------------------------

            await using (var db = new ApplicationDbContext(options))
            {
                var persistedTransaction = await db.Transactions
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == transactionId);

                Assert.Equal(
                    TransactionProcessingStatus.Completed,
                    persistedTransaction.ProcessingStatus);

                // Completed processing must not be rewritten.
                Assert.Equal(
                    originalProcessedAt,
                    persistedTransaction.ProcessedAt);

                // No duplicate alert.
                Assert.Equal(
                    1,
                    await db.Alerts.CountAsync(
                        a => a.TransactionId == transactionId));
            }

            // Redis counters must not increment again for the same transaction.
            Assert.Equal(
                accountCountBefore,
                await ReadCounterAsync(redisDb, accountCounterKey));

            Assert.Equal(
                deviceCountBefore,
                await ReadCounterAsync(redisDb, deviceCounterKey));

            Assert.Equal(
                ipCountBefore,
                await ReadCounterAsync(redisDb, ipCounterKey));
        }
        finally
        {
            // Stop the test-owned consumer before cleaning up its data.
            if (workerHost is not null)
            {
                try
                {
                    await workerHost.StopAsync();
                }
                finally
                {
                    workerHost.Dispose();
                }
            }

            // Delete only the Redis keys created for this transaction.
            foreach (var key in counterKeys.Concat(deduplicationKeys))
            {
                await redisDb.KeyDeleteAsync(key);
            }

            // Delete only this test's SQL records.
            await using var cleanupDb =
                new ApplicationDbContext(options);

            var alerts = await cleanupDb.Alerts
                .Where(a => a.TransactionId == transactionId)
                .ToListAsync();

            cleanupDb.Alerts.RemoveRange(alerts);

            var transaction = await cleanupDb.Transactions
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (transaction is not null)
            {
                cleanupDb.Transactions.Remove(transaction);
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

        return long.TryParse(value.ToString(), out var count)
            ? count
            : 0;
    }

    private static async Task<IHost> StartTestWorkerAsync(
        string sqlConnectionString,
        string redisConnectionString,
        string queueName)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = sqlConnectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Redis:ConnectionString"] = redisConnectionString
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(sqlConnectionString));

                // Register the Redis connection before registering RedisService.
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(redisConnectionString));

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

                // Match the production fraud-rule registrations.
                services.AddScoped<IFraudRule, HighAmountRule>();
                services.AddScoped<IFraudRule, VeryHighAmountRule>();
                services.AddScoped<IFraudRule, VelocityRule>();

                services.AddScoped<RuleEngine>();
                services.AddScoped<
                    IFraudProcessingService,
                    FraudProcessingService>();

                services.AddMassTransit(bus =>
                {
                    bus.AddConsumer<TransactionConsumer>();

                    bus.AddEntityFrameworkOutbox<ApplicationDbContext>(
                        options =>
                        {
                            options.UseSqlServer();
                        });

                    bus.UsingRabbitMq((busContext, rabbit) =>
                    {
                        rabbit.Host("localhost", "/", hostConfig =>
                        {
                            hostConfig.Username("guest");
                            hostConfig.Password("guest");
                        });

                        rabbit.ReceiveEndpoint(queueName, endpoint =>
                        {
                            endpoint.Durable = false;
                            endpoint.AutoDelete = true;

                            endpoint.UseMessageRetry(retry =>
                            {
                                retry.Intervals(
                                    TimeSpan.FromMilliseconds(250),
                                    TimeSpan.FromMilliseconds(500),
                                    TimeSpan.FromMilliseconds(750));
                            });

                            endpoint.UseEntityFrameworkOutbox<
                                ApplicationDbContext>(busContext);

                            endpoint.ConfigureConsumer<TransactionConsumer>(
                                busContext);
                        });
                    });
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static async Task WaitForInboxConsumedAsync(
        string connectionString,
        Guid messageId,
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            await using var connection =
                new Microsoft.Data.SqlClient.SqlConnection(connectionString);

            await connection.OpenAsync();

            await using var command = new Microsoft.Data.SqlClient.SqlCommand(
                """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.InboxState
                WHERE MessageId = @MessageId
                  AND Consumed IS NOT NULL
            )
            THEN 1 ELSE 0 END;
            """,
                connection);

            command.Parameters.Add(
                new Microsoft.Data.SqlClient.SqlParameter(
                    "@MessageId",
                    System.Data.SqlDbType.UniqueIdentifier)
                {
                    Value = messageId
                });

            var result = await command.ExecuteScalarAsync();

            if (Convert.ToInt32(result) == 1)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Inbox did not record successful consumption for MessageId {messageId}.");
    }

    // These assertions verify the durable business outcome and idempotent side effects.
    private static (
        Transaction Transaction,
        TransactionIngested Message) CreateTestCase(
            decimal amount,
            string merchant)
    {
        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var deviceId = $"inbox-test-device-{Guid.NewGuid():N}";
        var ipAddress = $"192.0.2.{Random.Shared.Next(1, 255)}";
        var timestamp = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = transactionId,
            AccountId = accountId,
            Amount = amount,
            Merchant = merchant,
            CreatedAt = timestamp,
            ProcessingStatus = TransactionProcessingStatus.Pending
        };

        var message = new TransactionIngested
        {
            TransactionId = transactionId,
            AccountId = accountId,
            Amount = amount,
            Merchant = merchant,
            Currency = "INR",
            Timestamp = timestamp,
            DeviceId = deviceId,
            IpAddress = ipAddress,
            Country = "IN",
            CorrelationId = Guid.NewGuid()
        };

        return (transaction, message);
    }

    private static async Task WaitForProcessingStatusAsync(
        DbContextOptions<ApplicationDbContext> options,
        Guid transactionId,
        TransactionProcessingStatus expectedStatus,
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            await using var db =
                new ApplicationDbContext(options);

            var status = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Id == transactionId)
                .Select(t => t.ProcessingStatus)
                .SingleAsync();

            if (status == expectedStatus)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Transaction {transactionId} did not reach " +
            $"{expectedStatus} within {timeout}.");
    }

    private static async Task WaitForInboxReceiveCountAsync(
        string connectionString,
        Guid messageId,
        int minimumCount,
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var count = await ReadInboxReceiveCountAsync(
                connectionString,
                messageId);

            if (count >= minimumCount)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"InboxState did not reach ReceiveCount {minimumCount} " +
            $"for MessageId {messageId} within {timeout}. " +
            "Check Consumer Outbox configuration and worker logs.");
    }

    private static async Task<int> ReadInboxReceiveCountAsync(
        string connectionString,
        Guid messageId)
    {
        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT COALESCE(MAX([ReceiveCount]), 0)
            FROM [dbo].[InboxState]
            WHERE [MessageId] = @MessageId;
            """,
            connection);

        command.Parameters.Add(
            new SqlParameter("@MessageId", SqlDbType.UniqueIdentifier)
            {
                Value = messageId
            });

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }
}
