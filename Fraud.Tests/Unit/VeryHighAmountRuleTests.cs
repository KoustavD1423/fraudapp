using Fraud.Application.Rules;
using Fraud.Contracts;
using Xunit;

namespace Fraud.Tests.Unit;

public class VeryHighAmountRuleTests
{
    [Fact]
    public async Task IsMatch_ShouldReturnTrue_WhenAmountIsAbove5000()
    {
        // Arrange
        var rule = new VeryHighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 6000m,
            Merchant = "Apple"
        };

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsMatch_ShouldReturnFalse_WhenAmountIsBelow5000()
    {
        // Arrange
        var rule = new VeryHighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 3000m,
            Merchant = "Apple"
        };

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsMatch_ShouldReturnFalse_WhenAmountIsExactly5000()
    {
        // Arrange
        var rule = new VeryHighAmountRule();

        var transaction = new TransactionIngested
        {
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 5000m,
            Merchant = "Apple"
        };

        // Act
        var result = await rule.IsMatchAsync(transaction);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Rule_ShouldHaveExpectedNameAndRiskScore()
    {
        // Arrange
        var rule = new VeryHighAmountRule();

        // Assert
        Assert.Equal("Very High Amount", rule.Name);
        Assert.Equal(0.95, rule.RiskScore);
    }
}