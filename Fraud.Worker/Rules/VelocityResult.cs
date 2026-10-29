namespace Fraud.Worker.Rules;

public class VelocityResult
{
    public long AccountCount { get; init; }

    public long DeviceCount { get; init; }

    public long IpCount { get; init; }

    public bool AccountExceeded { get; init; }

    public bool DeviceExceeded { get; init; }

    public bool IpExceeded { get; init; }

    public bool IsExceeded =>
        AccountExceeded ||
        DeviceExceeded ||
        IpExceeded;
}