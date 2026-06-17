using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PolicyManagement.Application.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that logs the entry, exit, and elapsed duration of every
/// handler. It is registered as the <strong>outermost</strong> behaviour so that it wraps
/// both validation and handler execution — timing and error logging therefore cover the
/// entire request lifecycle.
/// <para>
/// Pipeline execution order (outermost → innermost):
/// <code>
/// LoggingPipelineBehavior → ValidationPipelineBehavior → Handler
/// </code>
/// </para>
/// </summary>
/// <typeparam name="TRequest">The MediatR request type (command or query).</typeparam>
/// <typeparam name="TResponse">The handler response type.</typeparam>
public sealed class LoggingPipelineBehavior<TRequest, TResponse>(
    ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation(
            "Handling {RequestName}",
            requestName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();

            stopwatch.Stop();

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            logger.LogError(
                ex,
                "Error handling {RequestName} after {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
