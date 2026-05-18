using Fraud.Application.Interfaces;
using Fraud.Domain;
using Fraud.Application.Data;

namespace Fraud.Infrastructure;

public class TransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext _db;

    public TransactionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Transaction transaction)
    {
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync();
    }
}