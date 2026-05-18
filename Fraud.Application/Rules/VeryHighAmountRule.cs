using Fraud.Contracts;

namespace Fraud.Application.Rules;

public class VeryHighAmountRule : IFraudRule
{
    public string Name => "Very High Amount";
    public double RiskScore => 0.95;

    public bool IsMatch(TransactionIngested tx)
    {
        return tx.Amount > 5000;
    }
}