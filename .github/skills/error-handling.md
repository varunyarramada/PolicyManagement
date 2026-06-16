# Skill: Error Handling — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API · FluentValidation · MediatR

---

## Strategy Overview

All unhandled exceptions are caught by a single piece of middleware — `GlobalExceptionMiddleware` — registered at the outermost position in the ASP.NET Core pipeline. It is the only place where exceptions are converted to HTTP responses. No controller, handler, or pipeline behaviour ever writes an error response directly.

```
HTTP Request
     ↓
GlobalExceptionMiddleware  ← catches all unhandled exceptions here
     ↓
ASP.NET Core routing
     ↓
PoliciesController
     ↓
MediatR pipeline
     ↓  ValidationPipelineBehavior  → throws ValidationException
     ↓  Handler                     → throws DomainException subclass
     ↓  (unhandled)                 → propagates as Exception
```

Every exception that escapes the MediatR pipeline propagates up to `GlobalExceptionMiddleware`, which maps it to the appropriate HTTP status code and returns an RFC 7807 `ProblemDetails` response.

---

## RFC 7807 ProblemDetails Format

All error responses use RFC 7807 `application/problem+json`. This is the **only** error format the API returns — no custom envelopes, no plain strings, no raw exception messages.

### Standard fields

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | URI string | Yes | A URI reference identifying the problem type |
| `title` | string | Yes | Short, human-readable summary of the problem type |
| `status` | integer | Yes | HTTP status code |
| `detail` | string | No | Human-readable explanation specific to this occurrence |
| `instance` | URI string | No | URI reference identifying the specific occurrence |
| `correlationId` | string | No | Request correlation ID for log tracing |
| `errors` | object | No | Field-level validation errors — present only on `400` responses |

### Field-level errors (`400` only)

The `errors` extension property is a dictionary where each key is the field name (camelCase, matching the request schema) and each value is an array of error messages for that field.

### ProblemDetails response examples

**400 Bad Request — validation failure:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/policies/flag",
  "correlationId": "a3f2c1d4-8e7b-4a9f-b3c2-1d4e5f6a7b8c",
  "errors": {
    "policyIds": [
      "At least one policy ID must be provided.",
      "Cannot flag more than 100 policies in a single request."
    ],
    "page": [
      "Page number must be at least 1."
    ]
  }
}
```

**404 Not Found — domain exception:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Policy Not Found",
  "status": 404,
  "detail": "Policy with ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 was not found.",
  "instance": "/api/v1/policies/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "correlationId": "b4e3d2c1-9f8a-4b7e-c4d3-2e5f6a7b8c9d"
}
```

**409 Conflict — invalid state domain exception:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Invalid Policy State",
  "status": 409,
  "detail": "Policy POL-001234 cannot be flagged because it is already flagged for review.",
  "instance": "/api/v1/policies/flag",
  "correlationId": "c5f4e3d2-af9b-4c8f-d5e4-3f6a7b8c9d0e"
}
```

**500 Internal Server Error — unhandled exception:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "An unexpected error occurred. Please contact support with the correlation ID.",
  "instance": "/api/v1/policies",
  "correlationId": "d6a5f4e3-bf0c-4d9a-e6f5-4a7b8c9d0e1f"
}
```

---

## What Is Safe to Include in Error Responses

### Safe to include

- The HTTP status code
- A generic, user-facing `title` describing the problem type
- A `detail` message that references business-level facts (policy ID, policy number) — never internal system details
- The `correlationId` — allows the user or support to match the response to server-side logs
- Field-level `errors` for validation failures — field names and rule messages from validators
- The `instance` URI — identifies which endpoint produced the error

### Never include in error responses

| Prohibited | Why |
|---|---|
| Stack traces | Reveals internal code structure; aids attackers |
| Exception type names (e.g., `NullReferenceException`) | Leaks implementation detail |
| Connection strings or database server names | Direct security vulnerability |
| SQL error messages | Reveals schema details; aids SQL injection |
| Internal service names or IP addresses | Infrastructure enumeration |
| File paths from the server | Reveals directory structure |
| Raw `Exception.Message` for unhandled exceptions | May contain any of the above |

For `500` responses, the `detail` field is always a fixed, safe string — never the `Exception.Message`.

---

## Domain Exception Hierarchy

All domain exceptions live in `PolicyManagement.Domain/Exceptions/`. They inherit from `DomainException` — a project-defined base class. The base class carries the information `GlobalExceptionMiddleware` needs to map to the correct HTTP status code.

```csharp
// Domain/Exceptions/DomainException.cs
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message) { }
}
```

```csharp
// Domain/Exceptions/PolicyNotFoundException.cs
public sealed class PolicyNotFoundException : DomainException
{
    public Guid PolicyId { get; }

    public PolicyNotFoundException(Guid policyId)
        : base($"Policy with ID {policyId} was not found.")
    {
        PolicyId = policyId;
    }
}
```

```csharp
// Domain/Exceptions/InvalidPolicyStateException.cs
public sealed class InvalidPolicyStateException : DomainException
{
    public string PolicyNumber { get; }
    public string Reason { get; }

    public InvalidPolicyStateException(string policyNumber, string reason)
        : base($"Policy {policyNumber} is in an invalid state: {reason}")
    {
        PolicyNumber = policyNumber;
        Reason       = reason;
    }
}
```

### Exception-to-HTTP mapping

| Exception type | HTTP status | When thrown |
|---|---|---|
| `ValidationException` (FluentValidation) | `400 Bad Request` | `ValidationPipelineBehavior` — input fails validation rules |
| `PolicyNotFoundException` | `404 Not Found` | Handler — policy ID does not exist in the database |
| `InvalidPolicyStateException` | `409 Conflict` | Handler — domain invariant violated (e.g., already flagged) |
| `DomainException` (any unrecognised subclass) | `422 Unprocessable Entity` | Handler — any other domain rule failure |
| `Exception` (anything else) | `500 Internal Server Error` | Unhandled — infrastructure failure, unexpected null, etc. |

---

## GlobalExceptionMiddleware Implementation

```csharp
// API/Middleware/GlobalExceptionMiddleware.cs
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (PolicyNotFoundException ex)
        {
            await HandleDomainExceptionAsync(context, ex, StatusCodes.Status404NotFound,
                "Policy Not Found",
                "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (InvalidPolicyStateException ex)
        {
            await HandleDomainExceptionAsync(context, ex, StatusCodes.Status409Conflict,
                "Invalid Policy State",
                "https://tools.ietf.org/html/rfc7231#section-6.5.8");
        }
        catch (DomainException ex)
        {
            await HandleDomainExceptionAsync(context, ex, StatusCodes.Status422UnprocessableEntity,
                "Domain Rule Violation",
                "https://tools.ietf.org/html/rfc7231#section-6.5.13");
        }
        catch (Exception ex)
        {
            await HandleUnhandledExceptionAsync(context, ex);
        }
    }

    private async Task HandleValidationExceptionAsync(
        HttpContext context, ValidationException ex)
    {
        var correlationId = GetCorrelationId(context);

        _logger.LogWarning(
            "Validation failed for {Method} {Path}. CorrelationId: {CorrelationId}. Errors: {@Errors}",
            context.Request.Method,
            context.Request.Path,
            correlationId,
            ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

        var errors = ex.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        var problem = new ProblemDetails
        {
            Type     = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title    = "Validation Failed",
            Status   = StatusCodes.Status400BadRequest,
            Detail   = "One or more validation errors occurred.",
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["errors"]        = errors;

        await WriteProblemDetailsAsync(context, problem, StatusCodes.Status400BadRequest);
    }

    private async Task HandleDomainExceptionAsync(
        HttpContext context, DomainException ex,
        int statusCode, string title, string type)
    {
        var correlationId = GetCorrelationId(context);

        _logger.LogWarning(ex,
            "{ExceptionType} for {Method} {Path}. CorrelationId: {CorrelationId}",
            ex.GetType().Name,
            context.Request.Method,
            context.Request.Path,
            correlationId);

        var problem = new ProblemDetails
        {
            Type     = type,
            Title    = title,
            Status   = statusCode,
            Detail   = ex.Message,   // DomainException messages are safe to expose
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;

        await WriteProblemDetailsAsync(context, problem, statusCode);
    }

    private async Task HandleUnhandledExceptionAsync(
        HttpContext context, Exception ex)
    {
        var correlationId = GetCorrelationId(context);

        // Log full exception detail server-side
        _logger.LogError(ex,
            "Unhandled exception for {Method} {Path}. CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path,
            correlationId);

        // Return safe, generic message client-side — never ex.Message
        var problem = new ProblemDetails
        {
            Type     = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Title    = "An unexpected error occurred.",
            Status   = StatusCodes.Status500InternalServerError,
            Detail   = "An unexpected error occurred. Please contact support with the correlation ID.",
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = correlationId;

        await WriteProblemDetailsAsync(
            context, problem, StatusCodes.Status500InternalServerError);
    }

    private static async Task WriteProblemDetailsAsync(
        HttpContext context, ProblemDetails problem, int statusCode)
    {
        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string GetCorrelationId(HttpContext context)
        => context.TraceIdentifier;

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return propertyName;
        var parts = propertyName.Split('.');
        var last  = parts[^1];
        return char.ToLowerInvariant(last[0]) + last[1..];
    }
}
```

Register the middleware as the **first** middleware in the pipeline — before routing, authentication, and all other middleware — so it catches exceptions from every layer:

```csharp
// API/Program.cs
app.UseMiddleware<GlobalExceptionMiddleware>(); // must be first

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

---

## Correlation ID Strategy

Every request is assigned a correlation ID that links the HTTP response to the server-side log entries. `HttpContext.TraceIdentifier` is used as the correlation ID — it is set automatically by ASP.NET Core and is unique per request.

If the caller supplies a `X-Correlation-Id` header (common in distributed systems), respect it instead:

```csharp
private static string GetCorrelationId(HttpContext context)
{
    if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var id)
        && !string.IsNullOrWhiteSpace(id))
        return id.ToString();

    return context.TraceIdentifier;
}
```

The correlation ID is included in every error response under `extensions.correlationId`. It is also added to every log scope in handlers so that all log entries for a single request are queryable by this ID in a log aggregator.

---

## Logging Strategy

The logging principle is: **log full detail server-side, return safe detail client-side.**

| Exception type | Log level | What is logged |
|---|---|---|
| `ValidationException` | `Warning` | Method, path, correlation ID, all field errors |
| `PolicyNotFoundException` | `Warning` | Method, path, correlation ID, exception message |
| `InvalidPolicyStateException` | `Warning` | Method, path, correlation ID, exception message |
| Other `DomainException` | `Warning` | Method, path, correlation ID, exception message |
| Unhandled `Exception` | `Error` | Method, path, correlation ID, full exception with stack trace |

`Warning` is appropriate for domain exceptions because they represent expected exceptional paths — a policy not being found is not a server error. `Error` is used only when something genuinely unexpected has gone wrong.

The full exception (including stack trace) is always logged for `Error`-level events. It is never included in the client response.

```csharp
// Correct — full exception passed to ILogger, safe message to client
_logger.LogError(ex,
    "Unhandled exception for {Method} {Path}. CorrelationId: {CorrelationId}",
    context.Request.Method, context.Request.Path, correlationId);

// Wrong — exception message or stack trace written to response
problem.Detail = ex.Message;       // could contain connection strings, paths, etc.
problem.Detail = ex.StackTrace;    // reveals internal code structure
```

---

## How Middleware Integrates with the MediatR Pipeline

The middleware sits outside the MediatR pipeline. Exceptions thrown anywhere inside MediatR — whether in a pipeline behaviour or a handler — propagate up through the call stack and are caught by `GlobalExceptionMiddleware`.

```
GlobalExceptionMiddleware.InvokeAsync
    └── await _next(context)                 ← calls next middleware
            └── Controller.ActionMethod
                    └── _mediator.Send(...)
                            └── LoggingPipelineBehavior
                                    └── ValidationPipelineBehavior
                                            → throws ValidationException ──────┐
                                    └── Handler                                │
                                            → throws PolicyNotFoundException ──┤
                                            → throws InvalidPolicyStateException┤
                                            → any unhandled Exception ──────────┘
                                                                                │
    ← exception propagates up through all layers ───────────────────────────────┘
    GlobalExceptionMiddleware catch block executes
    → writes ProblemDetails response
```

Key integration points:

1. **`ValidationPipelineBehavior`** throws `FluentValidation.ValidationException` before the handler runs. The middleware catches it and returns `400` with the field-level `errors` map.

2. **Command/query handlers** throw typed `DomainException` subclasses (`PolicyNotFoundException`, `InvalidPolicyStateException`) when domain invariants are violated. The middleware catches each type and maps it to the correct status code.

3. **`LoggingPipelineBehavior`** re-throws after logging — it does not catch and swallow. The exception continues propagating to the middleware.

4. **No handler or controller** calls `context.Response.WriteAsync` or `return Problem(...)` directly for exceptions. All exception-to-response mapping is centralised in the middleware.

---

## Common Mistakes to Avoid

### Catching exceptions in handlers and returning null

```csharp
// WRONG — exception swallowed; middleware never sees it; client gets unexpected result
public async Task<PolicyDto?> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    try
    {
        return MapToDto(await _repository.GetByIdAsync(query.PolicyId, ct));
    }
    catch (Exception)
    {
        return null; // handler eats the exception
    }
}

// CORRECT — let exceptions propagate to GlobalExceptionMiddleware
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    var policy = await _repository.GetByIdAsync(query.PolicyId, ct)
        ?? throw new PolicyNotFoundException(query.PolicyId);
    return MapToDto(policy);
}
```

---

### Returning raw exception messages in domain exceptions

```csharp
// WRONG — DomainException.Message could contain unsafe details if constructed carelessly
public sealed class PolicyNotFoundException : DomainException
{
    public PolicyNotFoundException(Guid id, string connectionString)
        : base($"Could not find policy {id} using {connectionString}") // connection string in message!
    { }
}

// CORRECT — domain exception messages contain only business-safe facts
public sealed class PolicyNotFoundException : DomainException
{
    public PolicyNotFoundException(Guid id)
        : base($"Policy with ID {id} was not found.")
    { }
}
```

---

### Registering GlobalExceptionMiddleware after routing

```csharp
// WRONG — exceptions from routing itself are not caught
app.UseRouting();
app.UseMiddleware<GlobalExceptionMiddleware>(); // registered too late

// CORRECT — must be outermost; registered before everything else
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRouting();
```

---

### Throwing exceptions for control flow in handlers

```csharp
// WRONG — throwing an exception to check existence is not exceptional
public async Task<PolicyDto> Handle(GetPoliciesQuery query, CancellationToken ct)
{
    var policies = await _repository.GetPagedAsync(filter, ct);
    if (!policies.Items.Any())
        throw new PolicyNotFoundException(Guid.Empty); // empty list is not a 404
    return MapToPagedDto(policies);
}

// CORRECT — an empty result is a valid 200 response with an empty data array
public async Task<PagedResponse<PolicyDto>> Handle(GetPoliciesQuery query, CancellationToken ct)
{
    var policies = await _repository.GetPagedAsync(filter, ct);
    return MapToPagedDto(policies); // returns { data: [], pagination: { totalCount: 0 } }
}
```

---

### Defining domain exceptions outside the Domain layer

```csharp
// WRONG — domain exception defined in Application or Infrastructure
// Application/Exceptions/PolicyNotFoundException.cs  ← incorrect layer

// CORRECT — all domain exceptions live in Domain
// Domain/Exceptions/PolicyNotFoundException.cs       ← correct layer
```

Domain exceptions are part of the domain model. Application and Infrastructure layers throw them; they do not define them.
