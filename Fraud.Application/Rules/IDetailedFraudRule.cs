using Fraud.Contracts;

namespace Fraud.Application.Rules;

public interface IDetailedFraudRule : IFraudRule
{
    Task<FraudRuleResult> EvaluateDetailedAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default);
}