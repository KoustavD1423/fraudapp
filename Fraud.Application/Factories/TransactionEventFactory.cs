using Fraud.Application.Context;
using Fraud.Contracts;
using Fraud.Domain;

namespace Fraud.Application.Factories;

public class TransactionEventFactory : ITransactionEventFactory
{
    private readonly ITransactionContextProvider _contextProvider;

    public TransactionEventFactory(
        ITransactionContextProvider contextProvider)
    {
        _contextProvider = contextProvider;
    }

    public TransactionIngested Create(Transaction tx)
    {
        var request = _contextProvider.GetCurrentContext();

        return new TransactionIngested
        {
            TransactionId = tx.Id,

            AccountId = tx.AccountId,

            Amount = tx.Amount,

            Merchant = tx.Merchant,

            Currency = "INR",

            Timestamp = tx.CreatedAt,

            DeviceId = request.DeviceId,

            IpAddress = request.IpAddress,

            Country = request.Country,

            CorrelationId = request.CorrelationId
        };
    }
}