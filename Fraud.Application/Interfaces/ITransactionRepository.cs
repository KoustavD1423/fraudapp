using Fraud.Domain;

namespace Fraud.Application.Interfaces;

public interface ITransactionRepository
{
    Task AddAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}