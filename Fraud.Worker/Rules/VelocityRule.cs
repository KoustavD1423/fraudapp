using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Worker.Services;

namespace Fraud.Worker.Rules;

public class VelocityRule : IFraudRule
{
    private readonly RedisService _redis;

    public string Name => "High Velocity Transactions";
    public double RiskScore => 0.85;

    public VelocityRule(RedisService redis)
    {
        _redis = redis;
    }

    public async Task<bool> IsMatchAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default)
    {
        var key = $"velocity:{tx.AccountId}";

        var count = await _redis.IncrementWithExpiryAsync(
            key,
            TimeSpan.FromMinutes(1));

        return count > 5;
    }
}