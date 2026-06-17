using FluentValidation;
using MediatR;

namespace PolicyManagement.Application.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that runs all registered FluentValidation validators for the
/// incoming request before the handler executes. If any validator reports failures, a
/// <see cref="ValidationException"/> is thrown and the handler is never invoked.
/// <para>
/// Registration order matters. This behaviour runs <strong>after</strong>
/// <c>LoggingPipelineBehavior</c> so that validation failures are logged with the
/// correct handler context, and <strong>before</strong> the handler so that no
/// domain or persistence logic executes on an invalid request.
/// </para>
/// <para>
/// <c>GlobalExceptionMiddleware</c> in the API layer catches <see cref="ValidationException"/>
/// and maps it to an HTTP <c>400 Bad Request</c> response with field-level errors in the
/// RFC 7807 <c>ProblemDetails</c> body.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The MediatR request type (command or query).</typeparam>
/// <typeparam name="TResponse">The handler response type.</typeparam>
public sealed class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
