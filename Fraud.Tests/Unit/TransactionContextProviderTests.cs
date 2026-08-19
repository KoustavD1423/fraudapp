using Fraud.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Fraud.Tests.Unit;

public class TransactionContextProviderTests
{
    [Fact]
    public void GetCurrentContext_ShouldExtractValuesFromHttpContext()
    {
        // Arrange
        var correlationId = Guid.NewGuid();

        var httpContext = new DefaultHttpContext();

        httpContext.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse("192.168.1.100");

        httpContext.Request.Headers["User-Agent"] =
            "Test-Agent";

        httpContext.Items["CorrelationId"] =
            correlationId.ToString();

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        var provider = new TransactionContextProvider(accessor);

        // Act
        var result = provider.GetCurrentContext();

        // Assert
        Assert.Equal(
            "192.168.1.100",
            result.IpAddress);

        Assert.Equal(
            "Test-Agent",
            result.UserAgent);

        Assert.Equal(
            correlationId,
            result.CorrelationId);

        Assert.Equal(
            "India",
            result.Country);

        Assert.False(
            string.IsNullOrWhiteSpace(result.DeviceId));

        Assert.NotEqual(
            default,
            result.RequestTime);
    }


    [Fact]
    public void GetCurrentContext_ShouldGenerateCorrelationId_WhenNoCorrelationIdExists()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        var provider = new TransactionContextProvider(accessor);

        // Act
        var result = provider.GetCurrentContext();

        // Assert
        Assert.NotEqual(
            Guid.Empty,
            result.CorrelationId);
    }


    [Fact]
    public void GetCurrentContext_ShouldReturnUnknownIp_WhenHttpContextHasNoIp()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        var provider = new TransactionContextProvider(accessor);

        // Act
        var result = provider.GetCurrentContext();

        // Assert
        Assert.Equal(
            "Unknown",
            result.IpAddress);
    }


    [Fact]
    public void GetCurrentContext_ShouldReturnUnknownUserAgent_WhenHeaderDoesNotExist()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        var provider = new TransactionContextProvider(accessor);

        // Act
        var result = provider.GetCurrentContext();

        // Assert
        Assert.Equal(
            "Unknown",
            result.UserAgent);
    }
}