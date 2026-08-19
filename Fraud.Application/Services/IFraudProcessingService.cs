using Fraud.Contracts;

namespace Fraud.Application.Services;

public interface IFraudProcessingService
{
    Task ProcessAsync(
        TransactionIngested transaction,
        CancellationToken cancellationToken = default);
}