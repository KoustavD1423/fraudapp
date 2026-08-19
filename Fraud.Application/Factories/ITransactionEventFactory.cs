using Fraud.Contracts;
using Fraud.Domain;

namespace Fraud.Application.Factories;

public interface ITransactionEventFactory
{
    TransactionIngested Create(Transaction transaction);
}