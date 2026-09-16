using Fraud.Contracts;

namespace Fraud.Application.Rules;


public class HighAmountRule : IFraudRule
{
    public string Name => "High Amount";
    public double RiskScore => 0.9;

    public Task<bool> IsMatchAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(tx.Amount > 1000);
    }
}