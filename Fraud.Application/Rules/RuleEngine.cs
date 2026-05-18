using Fraud.Contracts;

namespace Fraud.Application.Rules;

public class RuleEngine
{
    private readonly IEnumerable<IFraudRule> _rules;

    public RuleEngine(IEnumerable<IFraudRule> rules)
    {
        _rules = rules;
    }

    public (bool isFraud, double score, List<string> reasons) Evaluate(TransactionIngested tx)
    {
        var matchedRules = _rules
            .Where(r => r.IsMatch(tx))
            .ToList();

        if (!matchedRules.Any())
            return (false, 0, new List<string>());

        double totalScore = matchedRules.Max(r => r.RiskScore);

        return (
            true,
            totalScore,
            matchedRules.Select(r => r.Name).ToList()
        );
    }
}