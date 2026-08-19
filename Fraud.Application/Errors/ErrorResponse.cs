namespace Fraud.Application.Errors;

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }
}