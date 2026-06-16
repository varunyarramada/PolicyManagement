# Skill: Production Readiness — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents, DevOps agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API · EF Core · SQL Server

---

## Health Check Endpoints

The API exposes two health check endpoints, following the Kubernetes liveness/readiness probe convention. Both are registered in `Program.cs` and must not leak sensitive infrastructure details in their responses.

### Liveness — `/health/live`

Answers the question: **is the application process running and responsive?**

A liveness check failing means the container should be restarted. It does not check external dependencies — if the database is down, the app is still alive. A liveness check that calls the database will cause cascading restarts during a database outage, which is incorrect behaviour.

Liveness checks:
- Application process is alive
- No unrecoverable internal state (deadlock, memory exhaustion)

### Readiness — `/health/ready`

Answers the question: **is the application ready to serve traffic?**

A readiness check failing means the load balancer should stop routing traffic to this instance — the app is alive but not ready. This is where external dependencies are checked.

Readiness checks for PolicyManagement:
- SQL Server is reachable and responding

### Registration

```csharp
// API/Program.cs
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration
            .GetRequiredSection(SqlServerOptions.SectionName)
            .GetValue<string>("ConnectionString")!,
        name: "sql-server",
        tags: new[] { "ready" });   // only included in /health/ready

// Map the two endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate      = _ => false,    // no checks — if the app is responding, it's alive
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate      = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});
```

Response writer — returns JSON without exposing internal connection details:

```csharp
// API/HealthChecks/HealthCheckResponseWriter.cs
private static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var result = JsonSerializer.Serialize(new
    {
        status  = report.Status.ToString(),
        checks  = report.Entries.Select(e => new
        {
            name    = e.Key,
            status  = e.Value.Status.ToString()
            // Do NOT include e.Value.Description or e.Value.Exception — these may contain
            // connection strings, server names, or stack traces
        })
    });

    return context.Response.WriteAsync(result);
}
```

Example liveness response:
```json
{ "status": "Healthy", "checks": [] }
```

Example readiness response:
```json
{
  "status": "Healthy",
  "checks": [{ "name": "sql-server", "status": "Healthy" }]
}
```

---

## Structured Logging

### Library

Use the built-in `ILogger<T>` throughout. Do not reference any logging framework (Serilog, NLog) directly in `Domain` or `Application` — only in `Infrastructure` and `API` for sink configuration.

### The fundamental rule — no string interpolation

```csharp
// WRONG — loses structured properties; not queryable in log aggregators
_logger.LogInformation($"Policy {policyId} retrieved for customer {customerId}");
_logger.LogError($"Failed to flag policy {policyId}: {ex.Message}");

// CORRECT — named parameters; queryable as structured fields
_logger.LogInformation(
    "Policy {PolicyId} retrieved for customer {CustomerId}",
    policyId, customerId);

_logger.LogError(ex,
    "Failed to flag policy {PolicyId}",
    policyId);
```

### Log level guidance

| Level | When to use | Examples |
|---|---|---|
| `Trace` | Fine-grained diagnostic; disabled in production | Loop iterations, cache key lookups |
| `Debug` | Developer diagnostics; disabled in production | Query parameters, resolved handler names |
| `Information` | Normal application flow | Request handled, policy retrieved, event published |
| `Warning` | Expected exceptional paths; not a bug | Policy not found, validation failure, cache miss after retry |
| `Error` | Unexpected failure; requires attention | Unhandled exception, database connection failed |
| `Critical` | Application cannot continue | Startup failure, unrecoverable state |

### Where logging belongs

| Concern | Location |
|---|---|
| Request start, duration, completion | `LoggingPipelineBehavior` (Application) |
| Request failure with exception | `LoggingPipelineBehavior` (Application) |
| Domain events raised (policy flagged) | Individual handler |
| Cache hit / miss | Individual handler |
| Unhandled exception formatting | `GlobalExceptionMiddleware` (API) |
| SQL errors, infrastructure failures | Repository or `GlobalExceptionMiddleware` |

Never add duplicate logging at multiple levels for the same event. If `LoggingPipelineBehavior` logs request failure, the handler does not also log it.

### Correlation ID in log scopes

Every log entry for a request should carry the correlation ID so all entries for a single request are queryable together in a log aggregator:

```csharp
// API/Middleware/CorrelationIdMiddleware.cs (or inside GlobalExceptionMiddleware)
public async Task InvokeAsync(HttpContext context)
{
    var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var id)
        ? id.ToString()
        : context.TraceIdentifier;

    using (_logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        await _next(context);
    }
}
```

With a scope active, every `ILogger` call within that request automatically includes `CorrelationId` in its structured properties — no need to pass it manually to every log statement.

---

## Configuration Management

### Strongly-typed options — `IOptions<T>`

Every configuration section is bound to a strongly-typed options class. `IConfiguration["key"]` is never called directly in business code.

```csharp
// Infrastructure/Options/SqlServerOptions.cs
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public string ConnectionString      { get; init; } = string.Empty;
    public int    CommandTimeoutSeconds { get; init; } = 30;
    public int    MaxRetryCount         { get; init; } = 3;
}

// Application/Interfaces/Options/CacheOptions.cs
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int DefaultTtlMinutes  { get; init; } = 5;
    public int SummaryTtlMinutes  { get; init; } = 1;
}
```

Registration in `Program.cs`:

```csharp
// API/Program.cs
builder.Services
    .Configure<SqlServerOptions>(
        builder.Configuration.GetSection(SqlServerOptions.SectionName))
    .Configure<CacheOptions>(
        builder.Configuration.GetSection(CacheOptions.SectionName));
```

Injection:

```csharp
// Application handler or Infrastructure service
public sealed class InMemoryCacheService : ICacheService
{
    private readonly CacheOptions _options;

    public InMemoryCacheService(IOptions<CacheOptions> options)
        => _options = options.Value;

    // Use _options.DefaultTtlMinutes — never IConfiguration directly
}
```

### Options validation at startup

Validate options at startup so misconfiguration fails fast — before the first request, not during one:

```csharp
// API/Program.cs
builder.Services
    .AddOptions<SqlServerOptions>()
    .Bind(builder.Configuration.GetSection(SqlServerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();   // fails at startup, not at first use
```

Add `[Required]` and range attributes to the options class:

```csharp
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString      { get; init; } = string.Empty;

    [Range(1, 300)]
    public int    CommandTimeoutSeconds { get; init; } = 30;
}
```

### Environment-specific configuration files

```
src/PolicyManagement.API/
├── appsettings.json               # Shared defaults — no secrets, no environment-specific values
├── appsettings.Development.json   # Local dev overrides — may reference LocalDB
├── appsettings.Test.json          # Test environment — InMemory database, reduced timeouts
└── appsettings.Production.json    # Placeholder only — real values from environment variables
```

`appsettings.Production.json` contains only non-secret defaults:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "SqlServer": {
    "CommandTimeoutSeconds": 30
  }
}
```

The connection string is never in any `appsettings` file committed to source control. It is supplied via environment variable:

```
SqlServer__ConnectionString=Server=prod-sql;Database=PolicyDb;...
```

ASP.NET Core's configuration system automatically maps `__`-separated environment variable names to nested JSON paths.

### Secrets — what must never be committed

| Category | Examples | Supply via |
|---|---|---|
| Connection strings | `Server=prod-sql;Password=...` | Environment variable or secrets manager |
| API keys | External service API keys | Environment variable or secrets manager |
| Signing keys | JWT signing secrets | Environment variable or secrets manager |
| Certificates | TLS private keys | Mounted volume or secrets manager |

For local development, use `dotnet user-secrets` — never `appsettings.Development.json` for actual secret values:

```powershell
dotnet user-secrets set "SqlServer:ConnectionString" "Server=(localdb)\\mssqllocaldb;..."
    --project src/PolicyManagement.API
```

---

## Swagger / OpenAPI UI

Swagger UI is enabled in development environments only. It is never exposed in production.

```csharp
// API/Program.cs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "PolicyManagement BFF API",
        Version     = "v1",
        Description = "Chubb APAC — Policy Management Backend for Frontend"
    });

    // Include XML comments for <summary> tags on controller actions
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// ...

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "PolicyManagement API v1");
        options.RoutePrefix = string.Empty; // serve at root in dev
    });
}
```

Enable XML documentation generation in the API project file:

```xml
<!-- src/PolicyManagement.API/PolicyManagement.API.csproj -->
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn> <!-- suppress missing XML comment warnings -->
</PropertyGroup>
```

---

## CORS Configuration

CORS is configured explicitly — `AllowAnyOrigin` is never used in production.

```csharp
// API/Program.cs
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            // Production: explicit allowed origins from configuration
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            policy.WithOrigins(allowedOrigins)
                  .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id")
                  .WithMethods("GET", "PATCH");
        }
    });
});

// ...

app.UseCors("FrontendPolicy");
```

---

## Response Compression

Enable response compression for JSON responses to reduce payload sizes over the network:

```csharp
// API/Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "application/problem+json" });
});

// ...

app.UseResponseCompression(); // must be before UseRouting
```

---

## Rate Limiting Considerations

Rate limiting is not implemented in the current BFF iteration but should be considered for production. ASP.NET Core 7+ includes built-in rate limiting via `Microsoft.AspNetCore.RateLimiting`.

Relevant policies for PolicyManagement:
- **Fixed window** on `PATCH /api/v1/policies/flag` — bulk flagging should be rate-limited to prevent abuse.
- **Sliding window** on `GET /api/v1/policies` — list queries with expensive filtering should be limited per client.
- **Concurrency limiter** as a global safety net — cap simultaneous in-flight requests.

Rate limit responses use HTTP `429 Too Many Requests` with a `Retry-After` header. If rate limiting is added, `429` must be declared in the OpenAPI spec and in `[ProducesResponseType]` annotations on affected endpoints.

---

## Graceful Shutdown and CancellationToken Propagation

ASP.NET Core sends a `CancellationToken` to controller actions when the server is shutting down or when the client disconnects. This token must be propagated through every async call so in-flight requests can be cancelled cleanly.

Rules:
- Every controller action has a `CancellationToken cancellationToken` parameter.
- Every MediatR `Handle` method has a `CancellationToken cancellationToken` parameter.
- Every repository method has a `CancellationToken ct` parameter and passes it to all EF Core calls.
- The token is never stored; it is always passed to the next awaitable call.

```csharp
// Controller — accepts and forwards
public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    => Ok(await _mediator.Send(new GetPolicyByIdQuery(id), cancellationToken));

// Handler — forwards to repository
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken cancellationToken)
{
    var policy = await _repository.GetByIdAsync(query.PolicyId, cancellationToken);
    ...
}

// Repository — forwards to EF Core
public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
    => await _context.Policies.AsNoTracking()
           .FirstOrDefaultAsync(p => p.Id == id, ct);
```

Graceful shutdown timeout is configured in `Program.cs`:

```csharp
// API/Program.cs
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

builder.Host.ConfigureHostOptions(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

---

## Docker Considerations

The PolicyManagement BFF is containerised as a single Docker image. Key principles:

### Dockerfile structure (multi-stage)

```dockerfile
# Stage 1 — Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/PolicyManagement.API/PolicyManagement.API.csproj",           "src/PolicyManagement.API/"]
COPY ["src/PolicyManagement.Application/PolicyManagement.Application.csproj", "src/PolicyManagement.Application/"]
COPY ["src/PolicyManagement.Domain/PolicyManagement.Domain.csproj",     "src/PolicyManagement.Domain/"]
COPY ["src/PolicyManagement.Infrastructure/PolicyManagement.Infrastructure.csproj", "src/PolicyManagement.Infrastructure/"]

RUN dotnet restore "src/PolicyManagement.API/PolicyManagement.API.csproj"

COPY . .
RUN dotnet publish "src/PolicyManagement.API/PolicyManagement.API.csproj" \
    -c Release -o /app/publish --no-restore

# Stage 2 — Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user — do not run as root in production
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PolicyManagement.API.dll"]
```

### Docker environment variable conventions

All secrets and environment-specific values are supplied as environment variables — never baked into the image:

```yaml
# docker-compose.yml (development only — not for production)
services:
  api:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - SqlServer__ConnectionString=Server=db;Database=PolicyDb;User Id=sa;Password=${SA_PASSWORD};
      - Cache__DefaultTtlMinutes=5
```

Environment variable names use `__` as the section separator (maps to JSON path `SqlServer.ConnectionString`).

### What must NOT be in the image

- Connection strings
- API keys or secrets
- `appsettings.Production.json` with real values
- `.env` files with secrets

---

## Complete `Program.cs` Registration Order

The order of middleware registration in `Program.cs` is significant. The following order is required for PolicyManagement:

```csharp
// API/Program.cs

var builder = WebApplication.CreateBuilder(args);

// --- Service registration ---
builder.Services
    .Configure<SqlServerOptions>(builder.Configuration.GetSection(SqlServerOptions.SectionName))
    .Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));

builder.Services.AddDbContext<PolicyDbContext>((sp, options) =>
{
    var sql = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
    options.UseSqlServer(sql.ConnectionString,
        o => o.CommandTimeout(sql.CommandTimeoutSeconds));
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();
builder.Services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(GetPolicyByIdQueryHandler).Assembly));

builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

builder.Services.AddValidatorsFromAssembly(typeof(FlagPoliciesCommandValidator).Assembly);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(/* ... */);
builder.Services.AddResponseCompression(/* ... */);
builder.Services.AddCors(/* ... */);
builder.Services.AddHealthChecks().AddSqlServer(/* ... */);

var app = builder.Build();

// --- Middleware pipeline (ORDER IS CRITICAL) ---
app.UseMiddleware<GlobalExceptionMiddleware>();  // 1. Outermost — catches all exceptions
app.UseResponseCompression();                    // 2. Before routing for full coverage
app.UseCors("FrontendPolicy");                  // 3. Before routing

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthentication();                         // When authentication is added
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live",  /* ... */);
app.MapHealthChecks("/health/ready", /* ... */);

// --- Startup tasks ---
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await PolicyDataSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<PolicyDbContext>());
}

await app.RunAsync();
```

---

## Production Readiness Checklist

Before promoting any build to production, verify:

- [ ] No connection strings, API keys, or secrets in any committed file
- [ ] `appsettings.Production.json` contains only safe defaults — all secrets supplied via environment variables
- [ ] Health check endpoints respond correctly (`/health/live`, `/health/ready`)
- [ ] Swagger UI is disabled (not reachable) in the production environment
- [ ] `EnableSensitiveDataLogging()` is only called when `IsDevelopment()`
- [ ] All `ILogger` calls use structured parameters — no string interpolation
- [ ] `CancellationToken` is accepted and forwarded in all controller actions, handlers, and repository methods
- [ ] Docker image runs as non-root user
- [ ] Response compression is enabled
- [ ] CORS policy uses explicit allowed origins — `AllowAnyOrigin` is not present
- [ ] Options classes are validated at startup with `ValidateOnStart()`
- [ ] Global exception middleware is the outermost middleware
- [ ] No stack traces or internal exception messages in any `ProblemDetails` response
