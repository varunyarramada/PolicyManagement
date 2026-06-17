using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.Domain.Exceptions;
using System.Text.Json;

namespace PolicyManagement.API.Middleware;

/// <summary>
/// Global exception-handling middleware. Catches all unhandled exceptions thrown during
/// request processing and maps them to RFC 7807 <see cref="ProblemDetails"/> responses.
/// Stack traces and internal exception messages are never exposed in responses.
/// </summary>
/// <remarks>
/// <para>
/// Must be registered <strong>before</strong> <c>UseAuthentication()</c> so that
/// ASP.NET Core's default bare 401/403 challenge responses are also intercepted and
/// wrapped as <c>ProblemDetails</c>.
/// </para>
/// <para>
/// Exception-to-status-code mapping:
/// <list type="table">
///   <listheader><term>Exception</term><description>HTTP status</description></listheader>
///   <item><term><see cref="PolicyNotFoundException"/></term><description>404 Not Found</description></item>
///   <item><term><see cref="InvalidPolicyStateException"/></term><description>409 Conflict</description></item>
///   <item><term><see cref="ValidationException"/></term><description>400 Bad Request (with field errors)</description></item>
///   <item><term>Any other</term><description>500 Internal Server Error</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? string.Empty;

        var (statusCode, title, detail, extensions) = exception switch
        {
            PolicyNotFoundException pnf => (
                StatusCodes.Status404NotFound,
                "Policy Not Found",
                pnf.Message,
                (IDictionary<string, object?>?)new Dictionary<string, object?>
                    { ["policyId"] = pnf.PolicyId }),

            InvalidPolicyStateException ips => (
                StatusCodes.Status409Conflict,
                "Invalid Policy State",
                ips.Message,
                (IDictionary<string, object?>?)new Dictionary<string, object?>
                    { ["policyId"] = ips.PolicyId }),

            ValidationException ve => (
                StatusCodes.Status400BadRequest,
                "Validation Failed",
                "One or more validation errors occurred.",
                (IDictionary<string, object?>?)new Dictionary<string, object?>
                {
                    ["errors"] = ve.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(
                            g => g.Key,
                            g => (object?)g.Select(e => e.ErrorMessage).ToArray())
                }),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred. Please try again later.",
                (IDictionary<string, object?>?)null)
        };

        // Log full details server-side; never expose in response
        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception {ExceptionType} on {Method} {Path}. CorrelationId: {CorrelationId}",
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                correlationId);
        }
        else
        {
            logger.LogWarning(
                "Handled exception {ExceptionType} → {StatusCode} on {Method} {Path}. CorrelationId: {CorrelationId}",
                exception.GetType().Name,
                statusCode,
                context.Request.Method,
                context.Request.Path,
                correlationId);
        }

        var problem = new ProblemDetails
        {
            Status   = statusCode,
            Title    = title,
            Detail   = detail,
            Instance = context.Request.Path,
        };

        problem.Extensions["correlationId"] = correlationId;

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
                problem.Extensions[key] = value;
        }

        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            context.RequestAborted);
    }
}
