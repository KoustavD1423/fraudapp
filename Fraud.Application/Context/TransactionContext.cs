namespace Fraud.Application.Context;

public class TransactionContext
{
    public string DeviceId { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    public DateTime RequestTime { get; set; }

    public string UserAgent { get; set; } = string.Empty;
}