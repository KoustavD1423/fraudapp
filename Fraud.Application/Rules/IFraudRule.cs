using Fraud.Contracts;

namespace Fraud.Application.Rules;

public interface IFraudRule
{
    string Name { get; }
    double RiskScore { get; }

    Task<bool> IsMatchAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default);
}