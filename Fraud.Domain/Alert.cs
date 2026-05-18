namespace Fraud.Domain;

public class Alert
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public double RiskScore { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}