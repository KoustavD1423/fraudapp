using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Domain;

namespace Fraud.Infrastructure;

public class TransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext _db;

    public TransactionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        await _db.Transactions.AddAsync(
            transaction,
            cancellationToken);

        // Do not call SaveChangesAsync here.
        // TransactionService commits the transaction and outbox message.
    }

    public Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}