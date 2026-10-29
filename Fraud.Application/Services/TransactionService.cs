using Fraud.Domain;
using Fraud.Application.Interfaces;
using Fraud.Contracts;
using Fraud.Application.Factories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Fraud.Application.Services;

public class TransactionService
{
    private readonly ITransactionRepository _repo;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ITransactionEventFactory _eventFactory;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ITransactionRepository repo,
        IPublishEndpoint publishEndpoint,
        ITransactionEventFactory eventFactory,
        ILogger<TransactionService> logger)
    {
        _repo = repo;
        _publishEndpoint = publishEndpoint;
        _eventFactory = eventFactory;
        _logger = logger;
    }

    public async Task<Transaction> CreateTransaction(
    Guid accountId,
    decimal amount,
    string merchant,
    CancellationToken cancellationToken = default)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Amount = amount,
            Merchant = merchant,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Creating transaction {TransactionId} for account {AccountId}. Amount: {Amount}, Merchant: {Merchant}",
            tx.Id,
            tx.AccountId,
            tx.Amount,
            tx.Merchant);

        // 1. Track the transaction. No database commit yet.
        await _repo.AddAsync(tx, cancellationToken);

        // 2. Create the integration event.
        var transactionEvent = _eventFactory.Create(tx);

        _logger.LogInformation(
            "Created TransactionIngested event for transaction {TransactionId} with CorrelationId {CorrelationId}",
            tx.Id,
            transactionEvent.CorrelationId);

        // 3. Publish through MassTransit's scoped Bus Outbox.
        // The outgoing message is stored in the EF Core context.
        await _publishEndpoint.Publish(
            transactionEvent,
            context =>
            {
                context.CorrelationId = transactionEvent.CorrelationId;
            },
            cancellationToken);

        // 4. Commit the transaction and outbox message together.
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId} and its outbox message committed successfully. CorrelationId: {CorrelationId}",
            tx.Id,
            transactionEvent.CorrelationId);

        return tx;
    }

}