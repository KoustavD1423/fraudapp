using Fraud.Application.Interfaces;
using StackExchange.Redis;

namespace Fraud.Worker.Services;

public class RedisService : IRedisService
{
    private readonly IDatabase _database;

    private const string IncrementWithExpiryScript = """
        local count = redis.call('INCR', KEYS[1])

        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end

        return count
        """;

    private const string IncrementOnceScript = """
    local previous = redis.call('GET', KEYS[2])

    if previous then
        return tonumber(previous)
    end

    local count = redis.call('INCR', KEYS[1])

    if count == 1 or redis.call('TTL', KEYS[1]) < 0 then
        redis.call('EXPIRE', KEYS[1], ARGV[1])
    end

    redis.call(
        'SET',
        KEYS[2],
        tostring(count),
        'EX',
        ARGV[2]
    )

    return count
    """;

    public RedisService(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);

        _database = redis.GetDatabase();
    }
    // the exisiting lua scriptimng for the redis service to incerment keys and anf have the proper ttl window if the service geyts called again and again
    public async Task<long> IncrementWithExpiryAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expirySeconds = (long)Math.Ceiling(
            expiry.TotalSeconds);

        if (expirySeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiry),
                "Expiry must be greater than zero.");
        }

        var result = await _database.ScriptEvaluateAsync(
            IncrementWithExpiryScript,
            new RedisKey[] { key },
            new RedisValue[] { expirySeconds });

        cancellationToken.ThrowIfCancellationRequested();

        return (long)result;
    }

    //The script atomically checks for a deduplication record before incrementing the counter.

//If the record exists, it returns the original count. If it doesn't exist, it increments the counter, sets its expiry when needed, and stores the original count under the deduplication key.

//This also protects against the case where Redis completes an operation but the client doesn't receive the response: a retry can retrieve the previously stored count.
    public async Task<long> IncrementOnceWithExpiryAsync(
    string counterKey,
    string deduplicationKey,
    TimeSpan counterExpiry,
    TimeSpan deduplicationExpiry,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(counterKey))
            throw new ArgumentException("Counter key cannot be empty.", nameof(counterKey));

        if (string.IsNullOrWhiteSpace(deduplicationKey))
            throw new ArgumentException("Deduplication key cannot be empty.", nameof(deduplicationKey));

        if (counterExpiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(counterExpiry));

        if (deduplicationExpiry < counterExpiry)
            throw new ArgumentOutOfRangeException(
                nameof(deduplicationExpiry),
                "Deduplication retention must be at least as long as the counter window.");

        var counterExpirySeconds = (long)Math.Ceiling(counterExpiry.TotalSeconds);
        var deduplicationExpirySeconds = (long)Math.Ceiling(deduplicationExpiry.TotalSeconds);

        if (counterExpirySeconds > int.MaxValue ||
            deduplicationExpirySeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deduplicationExpiry),
                "Redis expiry exceeds the supported range.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
            IncrementOnceScript,
            new RedisKey[] { counterKey, deduplicationKey },
            new RedisValue[]
            {
            counterExpirySeconds,
            deduplicationExpirySeconds
            });

        cancellationToken.ThrowIfCancellationRequested();

        return (long)result;
    }
}