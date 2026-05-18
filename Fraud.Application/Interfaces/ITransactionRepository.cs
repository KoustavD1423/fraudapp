namespace Fraud.Application.Interfaces;

using Fraud.Domain;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction);
}