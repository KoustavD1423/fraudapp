namespace Fraud.Application.Context;

public interface ITransactionContextProvider
{
    TransactionContext GetCurrentContext();
}