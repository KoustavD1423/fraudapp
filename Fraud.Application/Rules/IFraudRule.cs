using Fraud.Contracts;

namespace Fraud.Application.Rules;

public interface IFraudRule
{
    string Name { get; }
    double RiskScore { get; }

    bool IsMatch(TransactionIngested tx);
}