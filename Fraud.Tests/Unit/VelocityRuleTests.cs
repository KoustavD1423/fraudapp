using Fraud.Application.Configuration;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Worker.Rules;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fraud.Tests.Unit;

public class VelocityRuleTests
{
    [Fact]
    public async Task EvaluateVelocityAsync_ShouldIdentifyExceededDimensions()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 7);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 2);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 8);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.EvaluateVelocityAsync(transaction);

        // Assert
        Assert.Equal(8, result.AccountCount);
        Assert.Equal(3, result.DeviceCount);
        Assert.Equal(9, result.IpCount);

        Assert.True(result.AccountExceeded);
        Assert.False(result.DeviceExceeded);
        Assert.True(result.IpExceeded);
        Assert.True(result.IsExceeded);
    }

    [Fact]
    public async Task Should_ReturnFalse_When_NoThresholdIsExceeded()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 2);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 3);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 4);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Should_ReturnTrue_When_AccountVelocityExceedsThreshold()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 6);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 2);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 3);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Should_ReturnTrue_When_DeviceVelocityExceedsThreshold()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 2);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 6);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 3);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Should_ReturnTrue_When_IpVelocityExceedsThreshold()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 2);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 3);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 6);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Should_ReturnFalse_When_CountEqualsThreshold()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        // Each counter becomes 5 after the increment.
        redis.SetCount($"velocity:account:{transaction.AccountId}", 4);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 4);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 4);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateDetailedAsync_ShouldReturnReasonsForExceededDimensions()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        redis.SetCount($"velocity:account:{transaction.AccountId}", 7);
        redis.SetCount($"velocity:device:{transaction.DeviceId}", 2);
        redis.SetCount($"velocity:ip:{transaction.IpAddress}", 8);

        var rule = CreateRule(redis);

        // Act
        var result = await rule.EvaluateDetailedAsync(transaction);

        // Assert
        Assert.True(result.IsMatch);

        Assert.Contains("Account velocity exceeded", result.Reasons);
        Assert.DoesNotContain("Device velocity exceeded", result.Reasons);
        Assert.Contains("IP velocity exceeded", result.Reasons);
    }

    [Fact]
    public async Task EvaluateVelocityAsync_SameTransactionTwice_DoesNotIncrementCountersTwice()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();
        var rule = CreateRule(redis);

        // Act
        var firstResult = await rule.EvaluateVelocityAsync(transaction);
        var secondResult = await rule.EvaluateVelocityAsync(transaction);

        // Assert: the same transaction returns its original counts.
        Assert.Equal(1, firstResult.AccountCount);
        Assert.Equal(1, firstResult.DeviceCount);
        Assert.Equal(1, firstResult.IpCount);

        Assert.Equal(firstResult.AccountCount, secondResult.AccountCount);
        Assert.Equal(firstResult.DeviceCount, secondResult.DeviceCount);
        Assert.Equal(firstResult.IpCount, secondResult.IpCount);
    }

    [Fact]
    public async Task DifferentTransactionSameAccount_IncrementsAccountCounter()
    {
        // Arrange
        var redis = new FakeRedisService();

        var firstTransaction = CreateTransaction();
        var secondTransaction = CreateTransaction();

        // Same account, but different transaction, device, and IP.
        secondTransaction.AccountId = firstTransaction.AccountId;
        secondTransaction.DeviceId = "device-2";
        secondTransaction.IpAddress = "192.168.1.2";

        var rule = CreateRule(redis);

        // Act
        var firstResult = await rule.EvaluateVelocityAsync(firstTransaction);
        var secondResult = await rule.EvaluateVelocityAsync(secondTransaction);

        // Assert
        Assert.Equal(1, firstResult.AccountCount);
        Assert.Equal(2, secondResult.AccountCount);

        Assert.Equal(1, secondResult.DeviceCount);
        Assert.Equal(1, secondResult.IpCount);
    }

    [Fact]
    public async Task MissingDeviceAndInvalidIp_SkipThoseCounters()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();

        transaction.DeviceId = " ";
        transaction.IpAddress = "not-an-ip-address";

        var rule = CreateRule(redis);

        // Act
        var result = await rule.EvaluateVelocityAsync(transaction);

        // Assert
        Assert.Equal(1, result.AccountCount);
        Assert.Equal(0, result.DeviceCount);
        Assert.Equal(0, result.IpCount);
    }

    [Fact]
    public async Task RuleEngine_EvaluatesDetailedVelocityRuleOnlyOnce()
    {
        // Arrange
        var redis = new FakeRedisService();
        var transaction = CreateTransaction();
        var rule = CreateRule(redis);

        var engine = new RuleEngine(
            new IFraudRule[] { rule });

        // Act
        await engine.EvaluateAsync(transaction);

        // Assert:
        // One detailed rule evaluation should make one Redis call
        // per eligible dimension: account, device, and IP.
        Assert.Equal(3, redis.IncrementOnceCallCount);
    }

    private static VelocityRule CreateRule(FakeRedisService redis)
    {
        var options = Options.Create(
            new VelocityOptions
            {
                AccountThreshold = 5,
                DeviceThreshold = 5,
                IpThreshold = 5,
                WindowSeconds = 60,
                DeduplicationRetentionSeconds = 604800
            });

        return new VelocityRule(redis, options);
    }

    private static TransactionIngested CreateTransaction()
    {
        return new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),

            AccountId = Guid.Parse(
                "00000000-0000-0000-0000-000000000001"),

            DeviceId = "device-1",
            IpAddress = "192.168.1.1",
            Amount = 100,
            Merchant = "Test Merchant",
            Currency = "INR",
            Timestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
    }

    private sealed class FakeRedisService : IRedisService
    {
        // Current value of each counter.
        private readonly Dictionary<string, long> _counts = new();

        // Original count returned for each transaction/dimension.
        private readonly Dictionary<string, long> _deduplicatedCounts = new();

        // Counts calls to the idempotent Redis operation.
        public int IncrementOnceCallCount { get; private set; }

        public void SetCount(string key, long count)
        {
            _counts[key] = count;
        }

        public Task<long> IncrementWithExpiryAsync(
            string key,
            TimeSpan expiry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _counts.TryGetValue(key, out var count);

            count++;
            _counts[key] = count;

            return Task.FromResult(count);
        }

        public Task<long> IncrementOnceWithExpiryAsync(
            string counterKey,
            string deduplicationKey,
            TimeSpan counterExpiry,
            TimeSpan deduplicationExpiry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IncrementOnceCallCount++;

            if (string.IsNullOrWhiteSpace(counterKey))
            {
                throw new ArgumentException(
                    "Counter key cannot be empty.",
                    nameof(counterKey));
            }

            if (string.IsNullOrWhiteSpace(deduplicationKey))
            {
                throw new ArgumentException(
                    "Deduplication key cannot be empty.",
                    nameof(deduplicationKey));
            }

            if (counterExpiry <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(counterExpiry));
            }

            if (deduplicationExpiry < counterExpiry)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deduplicationExpiry),
                    "Deduplication retention must be at least as long as the counter window.");
            }

            // A retry returns the original count without incrementing.
            if (_deduplicatedCounts.TryGetValue(
                    deduplicationKey,
                    out var originalCount))
            {
                return Task.FromResult(originalCount);
            }

            // First evaluation for this transaction/dimension.
            _counts.TryGetValue(counterKey, out var count);

            count++;
            _counts[counterKey] = count;

            _deduplicatedCounts[deduplicationKey] = count;

            return Task.FromResult(count);
        }
    }
}