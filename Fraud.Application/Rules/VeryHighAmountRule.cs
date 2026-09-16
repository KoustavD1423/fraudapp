using Fraud.Contracts;

namespace Fraud.Application.Rules;

public class VeryHighAmountRule : IFraudRule
{
    public string Name => "Very High Amount";
    public double RiskScore => 0.95;

    public Task<bool> IsMatchAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(tx.Amount > 5000);
    }   
}