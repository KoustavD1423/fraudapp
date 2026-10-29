using Fraud.Application.Configuration;
using Fraud.Application.Interfaces;
using Fraud.Application.Rules;
using Fraud.Contracts;
using Fraud.Worker.Services;
using Microsoft.Extensions.Options;



namespace Fraud.Worker.Rules;
//keeping both original fraudRuleContract with IsMatchAsync() method and also keeping the detailed eval for fruadrule
//velocityRule gives detailed match status plus reasons in worker
//RuleEngine must call either isMatchAsync or EvaluateDetailedAsync based on the config but never both.
public class VelocityRule : IDetailedFraudRule
{
    private readonly IRedisService _redis;
    private readonly VelocityOptions _options;

    public string Name => "High Velocity Transactions";

    public double RiskScore => 0.85;

    public VelocityRule(
        IRedisService redis,
        IOptions<VelocityOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }
    public async Task<bool> IsMatchAsync(
    TransactionIngested tx,
    CancellationToken cancellationToken = default)
    {
        var result = await EvaluateVelocityAsync(
            tx,
            cancellationToken);

        return result.IsExceeded;
    }

    public async Task<VelocityResult> EvaluateVelocityAsync(
    TransactionIngested tx,
    CancellationToken cancellationToken = default)
    {


        var expiry = TimeSpan.FromSeconds(_options.WindowSeconds);

        var deduplicationExpiry = TimeSpan.FromSeconds(
            _options.DeduplicationRetentionSeconds);

        var transactionId = tx.TransactionId.ToString("N");

        var accountKey =
            $"velocity:account:{tx.AccountId}";

        var deviceKey =
            $"velocity:device:{tx.DeviceId}";

        var ipKey =
            $"velocity:ip:{tx.IpAddress}";

        var accountTask = _redis.IncrementOnceWithExpiryAsync(
        $"velocity:account:{tx.AccountId}",
        $"velocity:dedupe:account:{tx.AccountId}:{transactionId}",
        expiry,
        deduplicationExpiry,
        cancellationToken);

        Task<long>? deviceTask = null;

        if (!string.IsNullOrWhiteSpace(tx.DeviceId))
        {
            var deviceId = tx.DeviceId.Trim();

            deviceTask = _redis.IncrementOnceWithExpiryAsync(
                $"velocity:device:{deviceId}",
                $"velocity:dedupe:device:{deviceId}:{transactionId}",
                expiry,
                deduplicationExpiry,
                cancellationToken);
        }

        Task<long>? ipTask = null;

        if (System.Net.IPAddress.TryParse(
                tx.IpAddress?.Trim(),
                out var parsedIp))
        {
            var ipAddress = parsedIp.ToString();

            ipTask = _redis.IncrementOnceWithExpiryAsync(
                $"velocity:ip:{ipAddress}",
                $"velocity:dedupe:ip:{ipAddress}:{transactionId}",
                expiry,
                deduplicationExpiry,
                cancellationToken);
        }

        var pendingTasks = new List<Task> { accountTask };

        if (deviceTask is not null)
            pendingTasks.Add(deviceTask);

        if (ipTask is not null)
            pendingTasks.Add(ipTask);

        await Task.WhenAll(pendingTasks);

        var accountCount = await accountTask;
        var deviceCount = deviceTask is null ? 0L : await deviceTask;
        var ipCount = ipTask is null ? 0L : await ipTask;

        return new VelocityResult
        {
            AccountCount = accountCount,
            DeviceCount = deviceCount,
            IpCount = ipCount,

            AccountExceeded =
                accountCount > _options.AccountThreshold,

            DeviceExceeded =
                deviceCount > _options.DeviceThreshold,

            IpExceeded =
                ipCount > _options.IpThreshold
        };
    }
    public async Task<FraudRuleResult> EvaluateDetailedAsync(
    TransactionIngested tx,
    CancellationToken cancellationToken = default)
    {
        var velocity = await EvaluateVelocityAsync(
            tx,
            cancellationToken);

        var reasons = new List<string>();

        if (velocity.AccountExceeded)
            reasons.Add("Account velocity exceeded");

        if (velocity.DeviceExceeded)
            reasons.Add("Device velocity exceeded");

        if (velocity.IpExceeded)
            reasons.Add("IP velocity exceeded");

        return new FraudRuleResult
        {
            IsMatch = velocity.IsExceeded,
            Reasons = reasons
        };
    }
}