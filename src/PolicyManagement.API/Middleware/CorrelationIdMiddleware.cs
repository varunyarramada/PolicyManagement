namespace PolicyManagement.API.Middleware;

/// <summary>
/// Middleware that ensures every request carries a correlation ID.
/// Reads the <c>X-Correlation-ID</c> request header; generates a new <see cref="Guid"/>
/// if the header is absent or empty. The correlation ID is stored in
/// <c>HttpContext.Items["CorrelationId"]</c> and echoed back in the response header
/// <c>X-Correlation-ID</c>.
/// </summary>
/// <remarks>
/// Must be registered <strong>first</strong> in the middleware pipeline so that all
/// subsequent log entries (including auth failures and exception details) carry
/// the correlation ID.
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";
    private const string ItemsKey   = "CorrelationId";

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString();

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }
}
