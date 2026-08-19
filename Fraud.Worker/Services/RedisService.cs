using StackExchange.Redis;

namespace Fraud.Worker.Services;

public class RedisService
{
    private readonly IDatabase _database;

    public RedisService(IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("Redis") ?? "localhost:6379";

        var redis = ConnectionMultiplexer.Connect(connectionString);
        _database = redis.GetDatabase();
    }

    public async Task<long> IncrementWithExpiryAsync(
        string key,
        TimeSpan expiry)
    {
        var count = await _database.StringIncrementAsync(key);

        if (count == 1)
        {
            await _database.KeyExpireAsync(key, expiry);
        }

        return count;
    }
}