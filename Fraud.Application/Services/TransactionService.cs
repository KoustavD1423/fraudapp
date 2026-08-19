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
        string merchant)
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

        // Save transaction
        await _repo.AddAsync(tx);

        _logger.LogInformation(
            "Transaction {TransactionId} persisted successfully",
            tx.Id);

        // Create integration event
        var transactionEvent = _eventFactory.Create(tx);

        _logger.LogInformation(
            "Created TransactionIngested event for transaction {TransactionId} with CorrelationId {CorrelationId}",
            tx.Id,
            transactionEvent.CorrelationId);

        // Publish event
        await _publishEndpoint.Publish(
            transactionEvent,
            context =>
            {
                context.CorrelationId =
                    transactionEvent.CorrelationId;
            });

        _logger.LogInformation(
            "Transaction {TransactionId} published successfully with CorrelationId {CorrelationId}",
            tx.Id,
            transactionEvent.CorrelationId);

        return tx;
    }
}