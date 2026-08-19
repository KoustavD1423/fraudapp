using Fraud.Application.Context;
using Microsoft.AspNetCore.Http;

namespace Fraud.Api.Services;

public class TransactionContextProvider : ITransactionContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TransactionContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TransactionContext GetCurrentContext()
    {
        var context = _httpContextAccessor.HttpContext;

        var ip =
            context?.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown";

        var userAgent =
            context?.Request.Headers["User-Agent"].ToString();

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = "Unknown";
        }

        var correlationId =
            context?.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString();

        return new TransactionContext
        {
            DeviceId = Guid.NewGuid().ToString(),

            IpAddress = ip,

            Country = "India",

            UserAgent = userAgent,

            RequestTime = DateTime.UtcNow,

            CorrelationId = Guid.Parse(correlationId)
        };
    }
}

