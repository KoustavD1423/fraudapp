using Fraud.Contracts;

namespace Fraud.Application.Rules;

public class RuleEngine
{
    private readonly IEnumerable<IFraudRule> _rules;

    public RuleEngine(IEnumerable<IFraudRule> rules)
    {
        _rules = rules;
    }

    public async Task<(bool isFraud, double score, List<string> reasons)> EvaluateAsync(
    TransactionIngested tx,
    CancellationToken cancellationToken = default)
    {
        var matchedRules = new List<IFraudRule>();

        foreach (var rule in _rules)
        {
            if (await rule.IsMatchAsync(tx, cancellationToken))
            {
                matchedRules.Add(rule);
            }
        }

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