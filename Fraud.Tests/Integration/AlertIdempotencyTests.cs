using Fraud.Application.Data;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fraud.Tests.Integration;

public class AlertIdempotencyTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SameTransactionId_ShouldRepresentSameAlert()
    {
        // Arrange
        await using var db = CreateDbContext();

        var transactionId = Guid.NewGuid();

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            RiskScore = 0.9,
            Reason = "High Amount"
        };

        // Act
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        // Assert
        var existingAlert = await db.Alerts
            .SingleOrDefaultAsync(a => a.TransactionId == transactionId);

        Assert.NotNull(existingAlert);
        Assert.Equal(transactionId, existingAlert.TransactionId);
    }


    [Fact]
    public async Task DifferentTransactionIds_ShouldAllowDifferentAlerts()
    {
        // Arrange
        await using var db = CreateDbContext();

        var transactionId1 = Guid.NewGuid();
        var transactionId2 = Guid.NewGuid();

        // Act
        db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId1,
            RiskScore = 0.9,
            Reason = "High Amount"
        });

        db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId2,
            RiskScore = 0.9,
            Reason = "High Amount"
        });

        await db.SaveChangesAsync();

        // Assert
        var alerts = await db.Alerts.ToListAsync();

        Assert.Equal(2, alerts.Count);
        Assert.Contains(
            alerts,
            a => a.TransactionId == transactionId1);

        Assert.Contains(
            alerts,
            a => a.TransactionId == transactionId2);
    }
}