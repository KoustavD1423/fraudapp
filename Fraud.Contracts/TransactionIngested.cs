namespace Fraud.Contracts;

public class TransactionIngested
{
    // Identity
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }

    // Transaction Details
    public decimal Amount { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";

    // Context
    public DateTime Timestamp { get; set; }

    // Metadata
    public string DeviceId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    // Tracing
    public Guid CorrelationId { get; set; }

    public TransactionIngested()
    {
    }
}