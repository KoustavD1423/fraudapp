using Fraud.Contracts;

namespace Fraud.Application.Rules;

public class RuleEngine
{
    private readonly IEnumerable<IFraudRule> _rules;

    public RuleEngine(IEnumerable<IFraudRule> rules)
    {
        _rules = rules;
    }

    public async Task<(bool isFraud, double score, List<string> reasons)>
    EvaluateAsync(
        TransactionIngested tx,
        CancellationToken cancellationToken = default)
    {
        var matchedRules = new List<IFraudRule>();
        var reasons = new List<string>();

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule is IDetailedFraudRule detailedRule)
            {
                var result = await detailedRule.EvaluateDetailedAsync(
                    tx,
                    cancellationToken);

                if (result.IsMatch)
                {
                    matchedRules.Add(rule);
                    reasons.AddRange(result.Reasons);
                }
            }
            else
            {
                var isMatch = await rule.IsMatchAsync(
                    tx,
                    cancellationToken);

                if (isMatch)
                {
                    matchedRules.Add(rule);
                    reasons.Add(rule.Name);
                }
            }
        }

        if (matchedRules.Count == 0)
            return (false, 0, new List<string>());

        var totalScore = matchedRules.Max(
            rule => rule.RiskScore);

        return (true, totalScore, reasons);
    }
}

