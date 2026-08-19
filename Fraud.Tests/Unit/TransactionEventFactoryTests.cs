using Fraud.Application.Context;
using Fraud.Application.Factories;
using Fraud.Domain;
using Xunit;

namespace Fraud.Tests.Unit;

public class TransactionEventFactoryTests
{
    [Fact]
    public void Create_ShouldMapTransactionPropertiesToEvent()
    {
        // Arrange
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Amount = 1500m,
            Merchant = "Amazon",
            CreatedAt = DateTime.UtcNow
        };

        var expectedCorrelationId = Guid.NewGuid();

        var contextProvider = new FakeTransactionContextProvider(
            new TransactionContext
            {
                DeviceId = "device-123",
                IpAddress = "127.0.0.1",
                Country = "India",
                CorrelationId = expectedCorrelationId,
                RequestTime = DateTime.UtcNow,
                UserAgent = "Test-Agent"
            });

        var factory = new TransactionEventFactory(contextProvider);

        // Act
        var result = factory.Create(transaction);

        // Assert
        Assert.Equal(transaction.Id, result.TransactionId);
        Assert.Equal(transaction.AccountId, result.AccountId);
        Assert.Equal(transaction.Amount, result.Amount);
        Assert.Equal(transaction.Merchant, result.Merchant);
        Assert.Equal(transaction.CreatedAt, result.Timestamp);

        Assert.Equal("device-123", result.DeviceId);
        Assert.Equal("127.0.0.1", result.IpAddress);
        Assert.Equal("India", result.Country);
        Assert.Equal(expectedCorrelationId, result.CorrelationId);
    }

    private sealed class FakeTransactionContextProvider
        : ITransactionContextProvider
    {
        private readonly TransactionContext _context;

        public FakeTransactionContextProvider(TransactionContext context)
        {
            _context = context;
        }

        public TransactionContext GetCurrentContext()
        {
            return _context;
        }
    }
}