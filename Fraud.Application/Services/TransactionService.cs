using Fraud.Domain;
using Fraud.Application.Interfaces;
using Fraud.Contracts;
using MassTransit; // 🔥 REQUIRED

namespace Fraud.Application.Services;

public class TransactionService
{
    private readonly ITransactionRepository _repo;
    private readonly IPublishEndpoint _publishEndpoint;

    public TransactionService(
        ITransactionRepository repo,
        IPublishEndpoint publishEndpoint)
    {
        _repo = repo;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Transaction> CreateTransaction(Guid accountId, decimal amount, string merchant)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Amount = amount,
            Merchant = merchant,
            CreatedAt = DateTime.UtcNow
        };

        // Save transaction
        await _repo.AddAsync(tx);

        // 🔥 Publish event with CorrelationId
        await _publishEndpoint.Publish(new TransactionIngested
        {
            TransactionId = tx.Id,
            Amount = tx.Amount
        },
        context =>
        {
            context.CorrelationId = Guid.NewGuid();
        });

        return tx;
    }
}