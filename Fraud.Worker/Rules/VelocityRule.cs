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

    public bool IsMatch(TransactionIngested tx)
    {
        var key = $"velocity:{tx.AccountId}";

        var count = _redis
            .IncrementWithExpiryAsync(key, TimeSpan.FromMinutes(1))
            .GetAwaiter()
            .GetResult();

        return count > 5;
    }
}