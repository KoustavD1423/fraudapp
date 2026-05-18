using Fraud.Contracts;

namespace Fraud.Application.Rules;


public class HighAmountRule : IFraudRule
{
    public string Name => "High Amount";
    public double RiskScore => 0.9;

    public bool IsMatch(TransactionIngested tx)
    {
        return tx.Amount > 1000;
    }
}