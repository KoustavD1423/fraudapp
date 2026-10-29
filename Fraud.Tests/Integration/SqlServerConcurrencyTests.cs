using Fraud.Application.Data;
using Fraud.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fraud.Tests.Integration;

public class SqlServerConcurrencyTests
{
    [Fact]
    public async Task RowVersion_RejectsStaleUpdate()
    {
        // Use the connection string for your local SQL Server database.
        // Prefer a dedicated test database for integration testing.
        var connectionString =
            Environment.GetEnvironmentVariable("FRAUD_TEST_SQLSERVER");

        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set FRAUD_TEST_SQLSERVER to your SQL Server test database connection string.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString!)
            .Options;

        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        try
        {
            // 1. Seed one transaction.
            await using (var setupContext = new ApplicationDbContext(options))
            {
                setupContext.Transactions.Add(new Transaction
                {
                    Id = transactionId,
                    AccountId = accountId,
                    Amount = 100m,
                    Merchant = "SqlConcurrencyTest",
                    CreatedAt = DateTime.UtcNow,
                    ProcessingStatus = TransactionProcessingStatus.Pending
                });

                await setupContext.SaveChangesAsync();
            }

            // 2. Two independent contexts load the same database row.
            await using var contextA = new ApplicationDbContext(options);
            await using var contextB = new ApplicationDbContext(options);

            var transactionA = await contextA.Transactions
                .SingleAsync(t => t.Id == transactionId);

            var transactionB = await contextB.Transactions
                .SingleAsync(t => t.Id == transactionId);

            Assert.Equal(
                transactionA.RowVersion,
                transactionB.RowVersion);

            // 3. Worker A completes the transaction as legitimate.
            transactionA.ProcessingStatus =
                TransactionProcessingStatus.Completed;

            transactionA.IsFraud = false;
            transactionA.RiskScore = 0.0;
            transactionA.FraudReasons = string.Empty;
            transactionA.ProcessedAt = DateTime.UtcNow;

            await contextA.SaveChangesAsync();

            // 4. Worker B tries to save conflicting data using its
            // previously loaded RowVersion.
            transactionB.ProcessingStatus =
                TransactionProcessingStatus.Completed;

            transactionB.IsFraud = true;
            transactionB.RiskScore = 0.90;
            transactionB.FraudReasons = "Conflicting test result";
            transactionB.ProcessedAt = DateTime.UtcNow;

            // SQL Server should reject this stale update.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => contextB.SaveChangesAsync());

            // 5. Reload Worker B's entity to get the committed result.
            await contextB.Entry(transactionB).ReloadAsync();

            Assert.Equal(
                TransactionProcessingStatus.Completed,
                transactionB.ProcessingStatus);

            Assert.Equal(false, transactionB.IsFraud);
            Assert.Equal(0.0, transactionB.RiskScore);

            Assert.Equal(8, transactionB.RowVersion.Length);
        }
        finally
        {
            // Clean up only this test's transaction.
            await using var cleanupContext =
                new ApplicationDbContext(options);

            var testTransaction = await cleanupContext.Transactions
                .SingleOrDefaultAsync(t => t.Id == transactionId);

            if (testTransaction is not null)
            {
                cleanupContext.Transactions.Remove(testTransaction);
                await cleanupContext.SaveChangesAsync();
            }
        }
    }
}