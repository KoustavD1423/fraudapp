using Fraud.Application.Rules;
using Fraud.Contracts;
using Xunit;

namespace Fraud.Tests.Unit;

public class RuleEngineTests
{
    [Fact]
    public async Task Evaluate_ShouldReturnNoFraud_WhenNoRulesMatch()
    {
        // Arrange
        var rules = new IFraudRule[]
        {
            new HighAmountRule(),
            new VeryHighAmountRule()
        };

        var engine = new RuleEngine(rules);

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 500m,
            Merchant = "Amazon"
        };

        // Act
        var result = await engine.EvaluateAsync(transaction);

        // Assert
        Assert.False(result.isFraud);
        Assert.Equal(0, result.score);
        Assert.Empty(result.reasons);
    }

    [Fact]
    public async Task Evaluate_ShouldMatchHighAmountRule_WhenAmountIsAbove1000()
    {
        // Arrange
        var rules = new IFraudRule[]
        {
            new HighAmountRule(),
            new VeryHighAmountRule()
        };

        var engine = new RuleEngine(rules);

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 1500m,
            Merchant = "Amazon"
        };

        // Act
        var result = await engine.EvaluateAsync(transaction);

        // Assert
        Assert.True(result.isFraud);
        Assert.Equal(0.9, result.score);

        Assert.Single(result.reasons);
        Assert.Contains("High Amount", result.reasons);
    }

    [Fact]
    public async Task Evaluate_ShouldMatchBothRules_WhenAmountIsAbove5000()
    {
        // Arrange
        var rules = new IFraudRule[]
        {
            new HighAmountRule(),
            new VeryHighAmountRule()
        };

        var engine = new RuleEngine(rules);

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 6000m,
            Merchant = "Apple"
        };

        // Act
        var result = await engine.EvaluateAsync(transaction);

        // Assert
        Assert.True(result.isFraud);

        // Highest matching risk score should be selected
        Assert.Equal(0.95, result.score);

        Assert.Equal(2, result.reasons.Count);
        Assert.Contains("High Amount", result.reasons);
        Assert.Contains("Very High Amount", result.reasons);
    }

    [Fact]
    public async Task Evaluate_ShouldNotMatchHighAmountRule_WhenAmountIsExactly1000()
    {
        // Arrange
        var rules = new IFraudRule[]
        {
            new HighAmountRule(),
            new VeryHighAmountRule()
        };

        var engine = new RuleEngine(rules);

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 1000m,
            Merchant = "Amazon"
        };

        // Act
        var result = await engine.EvaluateAsync(transaction);

        // Assert
        Assert.False(result.isFraud);
        Assert.Equal(0, result.score);
        Assert.Empty(result.reasons);
    }

    [Fact]
    public async Task Evaluate_ShouldNotMatchVeryHighAmountRule_WhenAmountIsExactly5000()
    {
        // Arrange
        var rules = new IFraudRule[]
        {
            new HighAmountRule(),
            new VeryHighAmountRule()
        };

        var engine = new RuleEngine(rules);

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 5000m,
            Merchant = "Apple"
        };

        // Act
        var result = await engine.EvaluateAsync(transaction);

        // Assert
        Assert.True(result.isFraud);

        // HighAmountRule still matches
        Assert.Equal(0.9, result.score);

        Assert.Single(result.reasons);
        Assert.Contains("High Amount", result.reasons);
    }
}