using Fraud.Infrastructure;
using Fraud.Domain;
using Fraud.Application.Data;
using Fraud.Application.Interfaces;
using Fraud.Application.Events;
using Fraud.Application.Services;
using HotChocolate; // Ensure you have this for GraphQL-specific attributes/exceptions

namespace Fraud.Api.GraphQL;

public class Mutation
{
    public async Task<Transaction> AddTransaction(
        Guid accountId,
        decimal amount,
        string merchant,
        [Service] TransactionService service
        )
    {
       
        if (amount < 0)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Transaction amount cannot be negative.")
                    .SetCode("INVALID_AMOUNT")
                    .Build());
        }

        return await service.CreateTransaction(accountId, amount, merchant);
    }
}