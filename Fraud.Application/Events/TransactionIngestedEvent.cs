namespace Fraud.Application.Events;

public record TransactionIngestedEvent(Guid TransactionId, decimal Amount);