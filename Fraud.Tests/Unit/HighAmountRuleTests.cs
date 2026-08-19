using Fraud.Application.Rules;
using Fraud.Contracts;
using Xunit;

namespace Fraud.Tests.Unit;

public class HighAmountRuleTests
{
    [Fact]
    public void IsMatch_ShouldReturnTrue_WhenAmountIsAbove1000()
    {
        // Arrange
        var rule = new HighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 1500m,
            Merchant = "Amazon"
        };

        // Act
        var result = rule.IsMatch(transaction);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_WhenAmountIsBelow1000()
    {
        // Arrange
        var rule = new HighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 500m,
            Merchant = "Amazon"
        };

        // Act
        var result = rule.IsMatch(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_WhenAmountIsExactly1000()
    {
        // Arrange
        var rule = new HighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 1000m,
            Merchant = "Amazon"
        };

        // Act
        var result = rule.IsMatch(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Rule_ShouldHaveExpectedNameAndRiskScore()
    {
        // Arrange
        var rule = new HighAmountRule();

        // Assert
        Assert.Equal("High Amount", rule.Name);
        Assert.Equal(0.9, rule.RiskScore);
    }
}