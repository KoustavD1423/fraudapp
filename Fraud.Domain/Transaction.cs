namespace Fraud.Domain;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Merchant { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}