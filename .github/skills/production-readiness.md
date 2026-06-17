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
});  // Do NOT add .RequireAuthorization() — health checks must be accessible without auth

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate      = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});  // Do NOT add .RequireAuthorization() — health checks must be accessible without auth
```

**Critical:** Health check endpoints (`/health/live`, `/health/ready`) **must NOT** require authentication. They are infrastructure endpoints for container orchestration (Kubernetes liveness/readiness probes, Docker healthchecks). Never add `.RequireAuthorization()` to health check endpoint mappings. If the API requires authentication globally, health checks must be explicitly excluded from the authentication requirement.

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
| JWT configuration | `Jwt__Authority`, `Jwt__Audience` | Environment variable or secrets manager |
| Signing keys | JWT signing secrets, symmetric keys | Environment variable or secrets manager |
| Certificates | TLS private keys | Mounted volume or secrets manager |

For local development, use `dotnet user-secrets` — never `appsettings.Development.json` for actual secret values:

```powershell
dotnet user-secrets set "SqlServer:ConnectionString" "Server=(localdb)\\mssqllocaldb;..."
    --project src/PolicyManagement.API
```

### JWT Configuration

JWT configuration (Keycloak authority URL, audience, HTTPS enforcement) must be supplied via environment variables only. No JWT secrets or signing keys may appear in source code or `appsettings.json`.

**Required environment variables:**

| Variable | Description | Example (Development) | Example (Production) |
|----------|-------------|----------------------|---------------------|
| `Jwt__Authority` | Keycloak realm URL | `http://keycloak:8080/realms/policymanagement` | `https://keycloak.prod.chubb.com/realms/policymanagement` |
| `Jwt__Audience` | Keycloak client ID | `policymanagement-api` | `policymanagement-api` |
| `Jwt__RequireHttpsMetadata` | HTTPS enforcement for OIDC discovery | `false` | `true` |

**Registration with validation:**

```csharp
// API/Program.cs
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();   // Fails at startup — misconfiguration caught before first request
```

**Rules:**
- `JwtOptions` class uses `[Required]` attributes on `Authority` and `Audience` properties.
- `RequireHttpsMetadata` must be `true` in production — set to `false` only in local development when Keycloak runs on HTTP in Docker.
- Startup fails immediately if any required JWT configuration is missing or invalid.
- Never hardcode Keycloak URLs, client IDs, or secrets in source code or committed config files.

See [ADR-007](../../docs/architecture/decisions/ADR-007-jwt-bearer-authentication.md) for the authentication decision and [authentication.md](authentication.md) for implementation details.

---

## Security

### Authentication & Authorization

All four API endpoints require JWT Bearer authentication. The `PATCH /api/v1/policies/flag` endpoint additionally requires the `Policy.Write` role.

**Security requirements:**
- All API endpoints require a valid JWT Bearer token (`401 Unauthorized` if missing or invalid).
- `PATCH /flag` requires the `Policy.Write` role claim (`403 Forbidden` if missing).
- Both `401` and `403` responses are returned as RFC 7807 `ProblemDetails` with `application/problem+json` content type.
- Stack traces are **never** exposed in `401` or `403` responses.
- Token validation parameters:
  - `ValidateIssuer = true` — token must be issued by the configured Keycloak realm
  - `ValidateAudience = true` — token audience must match the configured client ID
  - `ValidateLifetime = true` — expired tokens are rejected
  - `ValidateIssuerSigningKey = true` — token signature must be valid
- `RoleClaimType` set to `"realm_access.roles"` for Keycloak compatibility.
- No cookie authentication — JWT Bearer only. Never use `AddCookie()` or session state.

**JWT Bearer Events — ProblemDetails formatting:**

```csharp
// API/Program.cs
.AddJwtBearer(options =>
{
    // ... Authority, Audience, TokenValidationParameters ...

    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();  // Suppress default 401
            // Return ProblemDetails with correlationId
        },
        OnForbidden = async context =>
        {
            // Return ProblemDetails with correlationId
        }
    };
});
```

See [authentication.md](authentication.md) for complete implementation guidance.

### Security Headers

Production deployments should include security headers. Add a middleware or use a reverse proxy to set:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Strict-Transport-Security: max-age=31536000; includeSubDomains` (HTTPS only)

Never expose:
- `Server` header (remove with `AddServerHeader = false` in Kestrel options)
- `X-Powered-By` header
- Detailed error messages with stack traces in production

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
    depends_on:
      - db
      - keycloak  # BFF depends on Keycloak being available
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - SqlServer__ConnectionString=Server=db;Database=PolicyDb;User Id=sa;Password=${SA_PASSWORD};
      - Cache__DefaultTtlMinutes=5
      - Jwt__Authority=http://keycloak:8080/realms/policymanagement
      - Jwt__Audience=policymanagement-api
      - Jwt__RequireHttpsMetadata=false
```

Environment variable names use `__` as the section separator (maps to JSON path `SqlServer.ConnectionString`).

### Keycloak Dependency

**Critical:** Keycloak must be running and accessible before the BFF starts accepting requests. The BFF validates JWT tokens by fetching public keys from Keycloak's OIDC discovery endpoint at startup.

**Startup sequence:**
1. Keycloak container starts and initializes.
2. Keycloak's OIDC discovery endpoint (`/.well-known/openid-configuration`) becomes available.
3. BFF container starts.
4. BFF fetches JWKS (JSON Web Key Set) from Keycloak during JWT Bearer configuration.
5. BFF begins accepting HTTP requests.

**In `docker-compose.yml`:**

```yaml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:26.0
    environment:
      - KEYCLOAK_ADMIN=admin
      - KEYCLOAK_ADMIN_PASSWORD=${KEYCLOAK_ADMIN_PASSWORD}
    ports:
      - "8081:8080"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 5

  api:
    depends_on:
      keycloak:
        condition: service_healthy  # Wait for Keycloak health check to pass
      db:
        condition: service_started
    environment:
      - Jwt__Authority=http://keycloak:8080/realms/policymanagement  # Internal Docker network URL
```

**Rules:**
- `Jwt__Authority` in the BFF environment variables points to the Keycloak realm URL **inside the Docker network** (e.g., `http://keycloak:8080/...`, not `http://localhost:8081/...`).
- The BFF must be able to reach Keycloak's OIDC discovery endpoint at startup — if Keycloak is unreachable, the BFF fails to start.
- Use `depends_on` with `condition: service_healthy` to ensure Keycloak is ready before the BFF starts.
- In production Kubernetes deployments, use readiness probes and init containers to enforce the same startup ordering.

### What must NOT be in the image

- Connection strings
- API keys or secrets
- `appsettings.Production.json` with real values
- `.env` files with secrets

---

## Integration Testing with Authentication

Integration tests use `WebApplicationFactory<Program>` to test the full HTTP request/response pipeline. Tests must **not** depend on a running Keycloak instance — JWT Bearer authentication is overridden with a test configuration that validates tokens signed with a known symmetric key.

### Test JWT Token Factory

Create a helper class to generate valid and invalid test tokens:

```csharp
// tests/PolicyManagement.API.Tests/Helpers/JwtTokenFactory.cs
internal static class JwtTokenFactory
{
    public const string TestSigningKey = "test-signing-key-must-be-at-least-32-chars!!";
    public const string TestIssuer = "test-issuer";
    public const string TestAudience = "policymanagement-api";

    public static string GenerateToken(
        string userId = "test-user-id",
        string email = "test@example.com",
        string[]? roles = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (roles != null)
            foreach (var role in roles)
                claims.Add(new Claim("realm_access.roles", role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateExpiredToken()
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "test-user-id") };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1),  // Expired 1 hour ago
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### WebApplicationFactory Override

Override JWT Bearer configuration in the test factory:

```csharp
// tests/PolicyManagement.API.Tests/CustomWebApplicationFactory.cs
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove production JWT Bearer registration
            var jwtDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConfigureOptions<JwtBearerOptions>));
            if (jwtDescriptor != null)
                services.Remove(jwtDescriptor);

            // Register test JWT Bearer with symmetric key
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = JwtTokenFactory.TestIssuer,
                        ValidateAudience = true,
                        ValidAudience = JwtTokenFactory.TestAudience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(JwtTokenFactory.TestSigningKey)),
                        RoleClaimType = "realm_access.roles"
                    };
                });
        });
    }
}
```

### Required Test Scenarios

Every integration test suite must cover these authentication scenarios:

| Scenario | Expected Result | Test |
|----------|----------------|------|
| No token provided | `401 Unauthorized` | Send request without `Authorization` header |
| Expired token | `401 Unauthorized` | Use `JwtTokenFactory.GenerateExpiredToken()` |
| Valid token, no specific role | `200 OK` on GET endpoints | Use `GenerateToken()` with no roles |
| Valid token, no specific role | `403 Forbidden` on `PATCH /flag` | Use `GenerateToken()` with no roles, call `/flag` |
| Valid token with `Policy.Write` role | `204 No Content` on `PATCH /flag` | Use `GenerateToken(roles: new[] { "Policy.Write" })` |

**Example test:**

```csharp
[Fact]
public async Task GetPolicies_WithoutToken_Returns401()
{
    // Arrange
    var client = _factory.CreateClient();
    // No Authorization header set

    // Act
    var response = await client.GetAsync("/api/v1/policies");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Status.Should().Be(401);
}

[Fact]
public async Task FlagPolicies_WithValidTokenButNoRole_Returns403()
{
    // Arrange
    var token = JwtTokenFactory.GenerateToken(roles: null);  // No Policy.Write role
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var request = new { policyIds = new[] { Guid.NewGuid() } };

    // Act
    var response = await client.PatchAsJsonAsync("/api/v1/policies/flag", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

See [authentication.md](authentication.md) Section 7 for complete integration test guidance.

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
- [ ] No JWT secrets, signing keys, or Keycloak credentials in source code or `appsettings.json`
- [ ] `appsettings.Production.json` contains only safe defaults — all secrets supplied via environment variables
- [ ] JWT configuration (`Jwt__Authority`, `Jwt__Audience`, `Jwt__RequireHttpsMetadata`) supplied via environment variables
- [ ] `Jwt__RequireHttpsMetadata` is `true` in production
- [ ] `JwtOptions` validated at startup with `ValidateOnStart()`
- [ ] Token validation parameters: `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true`
- [ ] `RoleClaimType` set to `"realm_access.roles"` for Keycloak
- [ ] Health check endpoints respond correctly (`/health/live`, `/health/ready`)
- [ ] Health check endpoints do **not** require authentication (no `.RequireAuthorization()`)
- [ ] Swagger UI is disabled (not reachable) in the production environment
- [ ] `EnableSensitiveDataLogging()` is only called when `IsDevelopment()`
- [ ] All `ILogger` calls use structured parameters — no string interpolation
- [ ] `CancellationToken` is accepted and forwarded in all controller actions, handlers, and repository methods
- [ ] Docker image runs as non-root user
- [ ] Keycloak is running and accessible before BFF starts (use `depends_on` with health checks in Docker Compose)
- [ ] `Jwt__Authority` points to Keycloak's internal Docker network URL, not localhost
- [ ] Response compression is enabled
- [ ] CORS policy uses explicit allowed origins — `AllowAnyOrigin` is not present
- [ ] Options classes are validated at startup with `ValidateOnStart()`
- [ ] Global exception middleware is the outermost middleware
- [ ] `GlobalExceptionMiddleware` registered **before** `UseAuthentication()`
- [ ] `JwtBearerEvents.OnChallenge` and `OnForbidden` return `ProblemDetails` (not bare status codes)
- [ ] No stack traces or internal exception messages in any `ProblemDetails` response
- [ ] Stack traces never exposed in `401` or `403` responses
- [ ] Integration tests cover: no token (401), expired token (401), valid token without role (403 on `/flag`), valid token with role (success)
- [ ] Integration tests do **not** depend on a running Keycloak instance (use test token factory)
