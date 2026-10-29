namespace Fraud.Domain;

public class Transaction
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public string Merchant { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Durable fraud-processing state.
    public TransactionProcessingStatus ProcessingStatus { get; set; }
        = TransactionProcessingStatus.Pending;

    public bool? IsFraud { get; set; }

    public double? RiskScore { get; set; }

    public string FraudReasons { get; set; } = string.Empty;

    public DateTime? ProcessedAt { get; set; }

    // SQL Server optimistic concurrency token.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}