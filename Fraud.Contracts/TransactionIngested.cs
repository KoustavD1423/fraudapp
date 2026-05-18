namespace Fraud.Contracts;

public class TransactionIngested
{
    public Guid TransactionId { get; set; }
    public decimal Amount { get; set; }

    public TransactionIngested() {}

    public TransactionIngested(Guid transactionId, decimal amount)
    {
        TransactionId = transactionId;
        Amount = amount;
    }
}