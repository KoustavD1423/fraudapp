namespace Fraud.Application.Interfaces;

public interface IRedisService
{
    Task<long> IncrementWithExpiryAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task<long> IncrementOnceWithExpiryAsync(
    string counterKey,
    string deduplicationKey,
    TimeSpan counterExpiry,
    TimeSpan deduplicationExpiry,
    CancellationToken cancellationToken = default);
}