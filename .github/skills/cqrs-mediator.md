# Skill: CQRS with MediatR — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API · MediatR

---

## What Is Logical CQRS and Why Is It Used Here

**CQRS** (Command Query Responsibility Segregation) separates the write side of the system (Commands) from the read side (Queries). In PolicyManagement this is **logical CQRS** — both sides share a single SQL Server database and a single EF Core `DbContext`. There is no separate read model database, no event sourcing, and no eventually-consistent projection pipeline.

### Why logical, not physical CQRS?

Physical CQRS introduces a separate read database, message bus synchronisation between the write and read stores, and eventual consistency. For a BFF serving an insurance dashboard with moderate load, this overhead is not justified. The benefits of logical CQRS are sufficient:

| Benefit | How it helps PolicyManagement |
|---|---|
| Separated concerns | Read handlers optimise for projection; write handlers enforce invariants |
| Independent scalability | Query handlers can be cached independently; command handlers are not cached |
| Simpler testing | Each handler has a single, clear input and output; easy to unit-test in isolation |
| Explicit intent | A `GetPoliciesQuery` cannot accidentally mutate state; a `FlagPoliciesCommand` cannot return a list |
| Pipeline extensibility | Cross-cutting concerns (validation, logging, caching) attach to the pipeline without touching handlers |

---

## Why MediatR Is Used as the In-Process Mediator

MediatR decouples the caller (controller) from the handler. The controller does not reference the handler class directly — it sends a message object and MediatR resolves and invokes the correct handler at runtime via the DI container.

This means:
- Controllers have no dependency on handler classes — only on the message types (`IRequest<T>` records).
- Adding a new query or command requires no changes to the controller.
- Pipeline behaviours (logging, validation, caching) attach declaratively and apply uniformly to all handlers.
- Handlers are independently registered and resolvable — easy to swap or extend.

MediatR uses the registered DI container to resolve handlers, so no custom factory or service locator is needed.

---

## Commands vs Queries

| Concern | Command | Query |
|---|---|---|
| **Purpose** | Change state | Read state |
| **Returns** | `Unit` (void) or a confirmation DTO | A DTO or collection |
| **Side effects** | Yes — updates database, publishes domain events | None — read-only |
| **Caching** | Never cached | May be cached via `ICacheService` |
| **Validation** | Always required before execution | Required for filter/pagination params |
| **Idempotency** | Should be designed to be idempotent | Naturally idempotent |

### Commands in PolicyManagement

| Command | Handler | What it does |
|---|---|---|
| `FlagPoliciesCommand` | `FlagPoliciesCommandHandler` | Bulk-flags a set of policies for review; publishes `PolicyFlaggedEvent` per policy |

### Queries in PolicyManagement

| Query | Handler | What it returns |
|---|---|---|
| `GetPoliciesQuery` | `GetPoliciesQueryHandler` | Paged, filtered, sorted list of `PolicyDto` |
| `GetPolicyByIdQuery` | `GetPolicyByIdQueryHandler` | Single `PolicyDto` by ID |
| `GetPolicySummaryQuery` | `GetPolicySummaryQueryHandler` | Aggregated stats — counts by status, region, premium totals |

---

## Naming Conventions

| Type | Pattern | Example |
|---|---|---|
| Command | `{Verb}{Entity}Command` | `FlagPoliciesCommand` |
| Command handler | `{Command}Handler` | `FlagPoliciesCommandHandler` |
| Command validator | `{Command}Validator` | `FlagPoliciesCommandValidator` |
| Query | `Get{Entity}By{Key}Query` or `Get{Entity}Query` | `GetPolicyByIdQuery`, `GetPoliciesQuery` |
| Query handler | `{Query}Handler` | `GetPolicyByIdQueryHandler` |
| Query validator | `{Query}Validator` | `GetPoliciesQueryValidator` |
| Pipeline behaviour | `{Name}PipelineBehavior` | `ValidationPipelineBehavior`, `LoggingPipelineBehavior` |
| Response DTO | `{Entity}Dto` or `{Entity}Response` | `PolicyDto`, `PolicySummaryResponse` |

All commands and queries are `record` types. All handlers are `sealed class` types.

---

## How to Structure Commands, Queries, and Handlers

Each command or query lives in its own folder under `Features/{Entity}/{Commands|Queries}/{FeatureName}/`. The folder contains the message, the handler, and — where required — the validator.

```
Application/
└── Features/
    └── Policies/
        ├── Commands/
        │   └── FlagPolicies/
        │       ├── FlagPoliciesCommand.cs
        │       ├── FlagPoliciesCommandHandler.cs
        │       └── FlagPoliciesCommandValidator.cs
        └── Queries/
            ├── GetPolicies/
            │   ├── GetPoliciesQuery.cs
            │   ├── GetPoliciesQueryHandler.cs
            │   └── GetPoliciesQueryValidator.cs
            ├── GetPolicyById/
            │   ├── GetPolicyByIdQuery.cs
            │   └── GetPolicyByIdQueryHandler.cs
            └── GetPolicySummary/
                ├── GetPolicySummaryQuery.cs
                └── GetPolicySummaryQueryHandler.cs
```

---

## Illustrative Code — Query

```csharp
// Application/Features/Policies/Queries/GetPolicyById/GetPolicyByIdQuery.cs
public record GetPolicyByIdQuery(Guid PolicyId) : IRequest<PolicyDto>;
```

```csharp
// Application/Features/Policies/Queries/GetPolicyById/GetPolicyByIdQueryHandler.cs
public sealed class GetPolicyByIdQueryHandler
    : IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{
    private readonly IPolicyRepository _repository;
    private readonly ICacheService _cache;
    private readonly ILogger<GetPolicyByIdQueryHandler> _logger;

    public GetPolicyByIdQueryHandler(
        IPolicyRepository repository,
        ICacheService cache,
        ILogger<GetPolicyByIdQueryHandler> logger)
    {
        _repository = repository;
        _cache      = cache;
        _logger     = logger;
    }

    public async Task<PolicyDto> Handle(
        GetPolicyByIdQuery query, CancellationToken cancellationToken)
    {
        var cacheKey = $"policy:v1:{query.PolicyId}";

        var cached = await _cache.GetAsync<PolicyDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;

        var policy = await _repository.GetByIdAsync(query.PolicyId, cancellationToken);

        if (policy is null)
            throw new PolicyNotFoundException(query.PolicyId);

        var dto = MapToDto(policy);

        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5), cancellationToken);

        _logger.LogInformation(
            "Policy {PolicyId} retrieved and cached", query.PolicyId);

        return dto;
    }

    private static PolicyDto MapToDto(Policy policy) => new(
        policy.Id,
        policy.PolicyNumber.Value,
        policy.Status.ToString(),
        policy.Premium.Amount,
        policy.Premium.Currency);
}
```

---

## Illustrative Code — Command

```csharp
// Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommand.cs
public record FlagPoliciesCommand(IReadOnlyList<Guid> PolicyIds) : IRequest;
// IRequest (no type parameter) = returns Unit (void equivalent)
```

```csharp
// Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandHandler.cs
public sealed class FlagPoliciesCommandHandler : IRequestHandler<FlagPoliciesCommand>
{
    private readonly IPolicyRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<FlagPoliciesCommandHandler> _logger;

    public FlagPoliciesCommandHandler(
        IPolicyRepository repository,
        IEventPublisher eventPublisher,
        ILogger<FlagPoliciesCommandHandler> logger)
    {
        _repository     = repository;
        _eventPublisher = eventPublisher;
        _logger         = logger;
    }

    public async Task Handle(
        FlagPoliciesCommand command, CancellationToken cancellationToken)
    {
        foreach (var policyId in command.PolicyIds)
        {
            var policy = await _repository.GetByIdAsync(policyId, cancellationToken);

            if (policy is null)
                throw new PolicyNotFoundException(policyId);

            policy.Flag();  // domain method enforces invariants

            await _repository.UpdateAsync(policy, cancellationToken);

            await _eventPublisher.PublishAsync(
                new PolicyFlaggedEvent(policy.Id, policy.PolicyNumber.Value),
                cancellationToken);

            _logger.LogInformation(
                "Policy {PolicyId} flagged for review", policyId);
        }
    }
}
```

---

## How to Dispatch from Controllers

Controllers are the only entry point into the MediatR pipeline from the HTTP layer. They do not call handlers, repositories, or services directly.

```csharp
// API/Controllers/PoliciesController.cs
[ApiController]
[Route("api/v1/policies")]
public sealed class PoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PoliciesController(IMediator mediator) => _mediator = mediator;

    // Query — returns data
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPolicyByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    // Command — returns no content
    [HttpPatch("flag")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flag(
        [FromBody] FlagPoliciesRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new FlagPoliciesCommand(request.PolicyIds), cancellationToken);
        return NoContent();
    }
}
```

Rules:
- `_mediator.Send(...)` is the only outbound call from a controller.
- Controllers never call `_repository`, `_cache`, or any application service directly.
- Controllers map HTTP request models to MediatR message records.
- Controllers map MediatR results to `IActionResult` responses.
- All controller actions accept and pass `CancellationToken`.

---

## MediatR Pipeline Behaviours

Pipeline behaviours are middleware for the MediatR pipeline. They wrap every handler invocation with cross-cutting logic — the same way ASP.NET Core middleware wraps HTTP requests.

A behaviour implements `IPipelineBehavior<TRequest, TResponse>` and is registered in the DI container. MediatR resolves all registered behaviours and chains them around the handler in registration order.

### Execution Order

Behaviours are applied **outside-in** — the first registered behaviour wraps all others. In PolicyManagement the ordering is:

```
HTTP Request
     ↓
Controller → _mediator.Send(query, ct)
     ↓
[1] LoggingPipelineBehavior    ← outermost — logs start, duration, completion
     ↓
[2] ValidationPipelineBehavior ← validates before the handler runs
     ↓
Handler (Query or Command)     ← innermost — executes business logic
     ↓
[2] ValidationPipelineBehavior ← (passes through on success)
     ↓
[1] LoggingPipelineBehavior    ← logs result / exception
     ↓
Controller → IActionResult
```

`LoggingPipelineBehavior` is outermost so it captures the total duration including validation time. `ValidationPipelineBehavior` short-circuits before the handler runs if validation fails — the handler is never invoked for an invalid request.

---

## Illustrative Code — ValidationPipelineBehavior

```csharp
// Application/Behaviours/ValidationPipelineBehavior.cs
public sealed class ValidationPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);
            // GlobalExceptionMiddleware catches this and returns 400 ProblemDetails

        return await next();
    }
}
```

Key points:
- If no `IValidator<TRequest>` is registered for a given request type, the behaviour passes through transparently — no error.
- `ValidationException` (FluentValidation) is caught by `GlobalExceptionMiddleware` and converted to a `400 Bad Request` `ProblemDetails` response with field-level error details.
- The handler (`next()`) is **never called** when validation fails.

---

## Illustrative Code — LoggingPipelineBehavior

```csharp
// Application/Behaviours/LoggingPipelineBehavior.cs
public sealed class LoggingPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingPipelineBehavior<TRequest, TResponse>> _logger;

    public LoggingPipelineBehavior(
        ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch   = Stopwatch.StartNew();

        _logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "Request {RequestName} failed after {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
```

Key points:
- Uses structured logging parameters — never string interpolation.
- Re-throws the exception after logging — does not swallow it.
- `GlobalExceptionMiddleware` in the API layer handles the exception and returns an appropriate `ProblemDetails` response.
- Elapsed time is measured using `Stopwatch` — not `DateTime.Now` arithmetic.

---

## How Validation Integrates with the Pipeline

Each command or query that requires input validation has a paired validator class in the same feature folder. The validator implements `AbstractValidator<T>` (FluentValidation).

```csharp
// Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandValidator.cs
public sealed class FlagPoliciesCommandValidator
    : AbstractValidator<FlagPoliciesCommand>
{
    public FlagPoliciesCommandValidator()
    {
        RuleFor(x => x.PolicyIds)
            .NotEmpty()
            .WithMessage("At least one policy ID must be provided.");

        RuleFor(x => x.PolicyIds)
            .Must(ids => ids.Count <= 100)
            .WithMessage("Cannot flag more than 100 policies in a single request.");

        RuleForEach(x => x.PolicyIds)
            .NotEmpty()
            .WithMessage("Policy ID must not be an empty GUID.");
    }
}
```

```csharp
// Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryValidator.cs
public sealed class GetPoliciesQueryValidator
    : AbstractValidator<GetPoliciesQuery>
{
    public GetPoliciesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page number must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");

        RuleFor(x => x.SortBy)
            .Must(BeAValidSortField)
            .When(x => x.SortBy is not null)
            .WithMessage("Invalid sort field.");
    }

    private static bool BeAValidSortField(string? field) =>
        field is "PolicyNumber" or "Status" or "Premium" or "CreatedAt";
}
```

The `ValidationPipelineBehavior` automatically discovers all registered `IValidator<T>` instances for the incoming request type. No wiring is needed in individual handlers.

Registration in `Program.cs`:
```csharp
// Register all validators from the Application assembly
builder.Services.AddValidatorsFromAssembly(
    typeof(FlagPoliciesCommandValidator).Assembly);

// Register the pipeline behaviour (open generic)
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(LoggingPipelineBehavior<,>));

builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(ValidationPipelineBehavior<,>));
```

---

## How Logging Integrates with the Pipeline

The `LoggingPipelineBehavior` wraps every handler — no per-handler logging boilerplate is needed for entry/exit and duration. Individual handlers still log domain-significant events (e.g., a specific policy was flagged), but they do not log request start/end or timing.

| Logging concern | Where it lives |
|---|---|
| Request start, duration, completion | `LoggingPipelineBehavior` |
| Request failure with exception | `LoggingPipelineBehavior` |
| Domain-significant events (policy flagged, cache hit/miss) | Individual handler |
| Unhandled exception formatting and response | `GlobalExceptionMiddleware` (API layer) |

All log messages use structured parameters:
```csharp
// Correct — structured, queryable in log aggregators
_logger.LogInformation(
    "Policy {PolicyId} flagged for review by {UserId}",
    policyId, userId);

// Wrong — plain string, not queryable
_logger.LogInformation($"Policy {policyId} flagged for review by {userId}");
```

---

## Complete Request Flow — HTTP to Database and Back

The following traces a `GET /api/v1/policies/{id}` request end-to-end:

```
1.  HTTP GET /api/v1/policies/{id}
        ↓
2.  ASP.NET Core routing → PoliciesController.GetById(id, ct)
        ↓
3.  Controller: _mediator.Send(new GetPolicyByIdQuery(id), ct)
        ↓
4.  MediatR resolves pipeline for GetPolicyByIdQuery
        ↓
5.  LoggingPipelineBehavior.Handle → logs "Handling GetPolicyByIdQuery"
        ↓
6.  ValidationPipelineBehavior.Handle
    → resolves IValidator<GetPolicyByIdQuery> (none registered → passes through)
        ↓
7.  GetPolicyByIdQueryHandler.Handle
    → checks ICacheService for "policy:v1:{id}"
    → cache hit  → returns cached PolicyDto  ─────────────────────────┐
    → cache miss → IPolicyRepository.GetByIdAsync(id, ct)             │
        ↓                                                              │
8.  PolicyRepository.GetByIdAsync                                     │
    → PolicyDbContext.Policies.AsNoTracking()                         │
      .FirstOrDefaultAsync(p => p.Id == id, ct)                      │
        ↓                                                              │
9.  SQL Server executes SELECT                                         │
        ↓                                                              │
10. Policy entity returned to handler                                  │
        ↓                                                              │
11. Handler maps Policy → PolicyDto                                    │
    → ICacheService.SetAsync("policy:v1:{id}", dto, 5min, ct)         │
        ↓                                                              │
12. PolicyDto returned up through pipeline  ◄──────────────────────────┘
        ↓
13. ValidationPipelineBehavior passes through
        ↓
14. LoggingPipelineBehavior logs duration
        ↓
15. Controller: return Ok(result)
        ↓
16. HTTP 200 OK { ... PolicyDto ... }
```

For a `PATCH /api/v1/policies/flag` command, step 8 becomes an `UpdateAsync` call per policy, step 11 raises `PolicyFlaggedEvent` via `IEventPublisher`, and no caching occurs.

---

## Common Mistakes to Avoid

### Putting business logic in controllers

```csharp
// WRONG
public async Task<IActionResult> GetById(Guid id)
{
    var policy = await _repository.GetByIdAsync(id); // direct repo call
    if (policy.Status == "Expired")
        return Forbid();                              // domain rule in HTTP layer
    return Ok(MapToDto(policy));
}

// CORRECT — all logic in the handler
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new GetPolicyByIdQuery(id), ct);
    return Ok(result);
}
```

---

### Sharing one handler across multiple request types

```csharp
// WRONG — one handler for two queries; violates single responsibility
public class PolicyQueryHandler
    : IRequestHandler<GetPoliciesQuery, PagedResponse<PolicyDto>>,
      IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{ ... }

// CORRECT — one handler per request type
public sealed class GetPoliciesQueryHandler
    : IRequestHandler<GetPoliciesQuery, PagedResponse<PolicyDto>> { ... }

public sealed class GetPolicyByIdQueryHandler
    : IRequestHandler<GetPolicyByIdQuery, PolicyDto> { ... }
```

---

### Calling `_mediator.Send` from inside a handler

```csharp
// WRONG — handler dispatches another command internally; creates hidden coupling
public async Task Handle(FlagPoliciesCommand command, CancellationToken ct)
{
    await _mediator.Send(new AuditCommand(...), ct); // nested dispatch
    ...
}

// CORRECT — call the repository or service directly; or raise a domain event
public async Task Handle(FlagPoliciesCommand command, CancellationToken ct)
{
    ...
    await _eventPublisher.PublishAsync(new PolicyFlaggedEvent(...), ct);
}
```

---

### Skipping `CancellationToken` propagation

```csharp
// WRONG — token not forwarded; long-running query cannot be cancelled
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    => MapToDto(await _repository.GetByIdAsync(query.PolicyId));

// CORRECT
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    => MapToDto(await _repository.GetByIdAsync(query.PolicyId, ct));
```

---

### Catching and swallowing exceptions in pipeline behaviours

```csharp
// WRONG — exception swallowed; client receives no error; handler failure is silent
catch (Exception)
{
    return default!;
}

// CORRECT — log then re-throw; let GlobalExceptionMiddleware handle the response
catch (Exception ex)
{
    _logger.LogError(ex, "Request {RequestName} failed", typeof(TRequest).Name);
    throw;
}
```

---

### Returning domain entities from handlers instead of DTOs

```csharp
// WRONG — exposes domain internals; couples API contract to entity shape
public async Task<Policy> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    => await _repository.GetByIdAsync(query.PolicyId, ct);

// CORRECT — map to DTO before returning; domain entity stays within Application
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    var policy = await _repository.GetByIdAsync(query.PolicyId, ct)
        ?? throw new PolicyNotFoundException(query.PolicyId);
    return MapToDto(policy);
}
```

---

### Putting validation logic inside handlers

```csharp
// WRONG — validation duplicated in handler; bypasses pipeline behaviour
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    if (query.PolicyId == Guid.Empty)
        throw new ArgumentException("PolicyId must not be empty.");
    ...
}

// CORRECT — validation belongs in the validator; handler trusts the input is valid
public sealed class GetPolicyByIdQueryValidator : AbstractValidator<GetPolicyByIdQuery>
{
    public GetPolicyByIdQueryValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
```
