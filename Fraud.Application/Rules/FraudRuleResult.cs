namespace Fraud.Application.Rules;

public class FraudRuleResult
{
    public bool IsMatch { get; init; }

    public List<string> Reasons { get; init; } = new();
}