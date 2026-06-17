# Skill: Authentication & Authorization — PolicyManagement BFF

**Audience:** Backend Developer agent, QA Engineer agent, DevOps Engineer agent, Reviewer agent
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API
**Decision:** [ADR-007](../../docs/architecture/decisions/ADR-007-jwt-bearer-authentication.md)

---

## Overview

The PolicyManagement BFF uses **JWT Bearer authentication** with **Keycloak** as the external identity provider. The BFF validates tokens — it never issues them.

- All four API endpoints require a valid JWT Bearer token (`401 Unauthorized` if absent or invalid).
- `PATCH /api/v1/policies/flag` additionally requires the `Policy.Write` role (`403 Forbidden` if missing).
- Read endpoints require a valid token but no specific role.
- User identity is exposed to handlers through `ICurrentUserService` — handlers never access `HttpContext` directly.
- Health check endpoints (`/health/live`, `/health/ready`) do **not** require authentication.

---

## Section 1 — JWT Bearer Setup in Program.cs

### NuGet Package

Install in `PolicyManagement.API.csproj` only. Never install auth packages in `Domain` or `Application`.

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
```

### Bind and Validate JwtOptions First

```csharp
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()!;
```

### Register Authentication

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtOptions.Authority;
        options.Audience = jwtOptions.Audience;
        options.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "realm_access.roles"  // Keycloak-specific role claim path
        };
    });
```

> **Why `realm_access.roles`?** Keycloak embeds realm roles in a nested JSON claim at `realm_access.roles`, not in the standard `roles` or `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` claim. Setting `RoleClaimType` to this path ensures `User.IsInRole()` and `[Authorize(Roles = "...")]` work correctly with Keycloak-issued tokens.

### Register Authorization

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PolicyWrite", policy =>
        policy.RequireRole("Policy.Write"));
});
```

### Register ICurrentUserService

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
```

---

## Section 2 — Middleware Pipeline Order

The order of middleware registration in `Program.cs` is critical for correct behaviour.

```csharp
// 1. Correlation ID — must be first so all log entries carry the ID
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Global exception handler — must be before auth so that 401/403
//    failures are caught and returned as ProblemDetails, not raw
//    ASP.NET Core challenge responses
app.UseMiddleware<GlobalExceptionMiddleware>();

// 3. Authentication — validates the JWT token, populates HttpContext.User
app.UseAuthentication();

// 4. Authorization — evaluates [Authorize] policies against HttpContext.User
app.UseAuthorization();

// 5. Endpoint routing — routes requests to controllers
app.MapControllers();

// 6. Health checks — no authentication required (infrastructure endpoints)
app.MapHealthChecks("/health/live", ...);
app.MapHealthChecks("/health/ready", ...);
```

**Why `GlobalExceptionMiddleware` before `UseAuthentication()`?**

When JWT validation fails, ASP.NET Core's JWT Bearer middleware calls `context.ChallengeAsync()`, which by default returns a plain `401` response with no body. Placing `GlobalExceptionMiddleware` before `UseAuthentication()` intercepts these challenge/forbid events and wraps them in RFC 7807 `ProblemDetails` format — consistent with all other error responses in the API.

**Why health checks after `MapControllers()`?**

Health check endpoints are mapped after controllers and are **not** wrapped with `[Authorize]`. They must remain publicly accessible for container orchestration liveness and readiness probes. Never add `RequireAuthorization()` to health check endpoint mappings.

---

## Section 3 — Controller Authorization Attributes

Apply `[Authorize]` at the **controller class level** to protect all actions by default. Apply `[Authorize(Policy = "PolicyWrite")]` at the **action level** for the flag endpoint only.

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]                          // Applies to all actions — requires valid JWT
[Produces("application/json")]
public sealed class PoliciesController : ControllerBase
{
    // GET /api/v1/policies — requires valid token, no specific role
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<PolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPolicies([FromQuery] GetPoliciesQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    // GET /api/v1/policies/{id} — requires valid token, no specific role
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPolicyById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetPolicyByIdQuery(id), ct));

    // GET /api/v1/policies/summary — requires valid token, no specific role
    [HttpGet("summary")]
    [ProducesResponseType(typeof(PolicySummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await _mediator.Send(new GetPolicySummaryQuery(), ct));

    // PATCH /api/v1/policies/flag — requires Policy.Write role
    [HttpPatch("flag")]
    [Authorize(Policy = "PolicyWrite")]  // Additional role check on top of [Authorize] above
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FlagPolicies([FromBody] FlagPoliciesCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }
}
```

**Rules:**
- `[Authorize]` on the class — never omit it.
- `[Authorize(Policy = "PolicyWrite")]` on the flag action — stacks on top of the class-level `[Authorize]`.
- Never use `[AllowAnonymous]` on any policy endpoint.
- Every action must declare `[ProducesResponseType]` for `401` and, where applicable, `403`.

---

## Section 4 — ICurrentUserService

### Interface (Application Layer)

Defined in `PolicyManagement.Application/Interfaces/ICurrentUserService.cs`. Zero dependency on ASP.NET Core types — this interface must compile without any Microsoft.AspNetCore.* reference.

```csharp
namespace PolicyManagement.Application.Interfaces;

public interface ICurrentUserService
{
    /// <summary>JWT sub claim — the authenticated user's unique identifier.</summary>
    string UserId { get; }

    /// <summary>JWT email claim.</summary>
    string Email { get; }

    /// <summary>All roles extracted from the JWT realm_access.roles claim.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Returns true if the current user has the specified role.</summary>
    bool IsInRole(string role);
}
```

### Implementation (API Layer)

Defined in `PolicyManagement.API/Services/CurrentUserService.cs`. This is the only class in the project allowed to access `IHttpContextAccessor` and `ClaimsPrincipal`.

```csharp
namespace PolicyManagement.API.Services;

internal sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal User =>
        _httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context available.");

    public string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? string.Empty;

    public string Email =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email")
        ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        User.FindAll("realm_access.roles")
            .Select(c => c.Value)
            .ToList()
            .AsReadOnly();

    public bool IsInRole(string role) => User.IsInRole(role);
}
```

**Rules:**
- Registered as `Scoped` — one instance per HTTP request.
- Handlers that need the current user inject `ICurrentUserService` via constructor injection.
- Handlers never receive `HttpContext`, `ClaimsPrincipal`, or `IHttpContextAccessor` directly.
- `Domain` layer has zero awareness of `ICurrentUserService`.

---

## Section 5 — JwtOptions Configuration Class

Defined in `PolicyManagement.API/Configuration/JwtOptions.cs` (or `PolicyManagement.Infrastructure/Configuration/JwtOptions.cs`).

```csharp
using System.ComponentModel.DataAnnotations;

namespace PolicyManagement.API.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Keycloak realm URL. Example: http://keycloak:8080/realms/policymanagement
    /// JWT Bearer middleware uses this for OIDC discovery and JWKS key retrieval.
    /// </summary>
    [Required]
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Keycloak client ID. Example: policymanagement-api
    /// Must match the audience claim in the JWT.
    /// </summary>
    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Set to true in production (Keycloak runs behind HTTPS).
    /// Set to false in development (Keycloak runs on HTTP in Docker).
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;
}
```

### Registration in Program.cs

```csharp
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();   // Fails fast at startup — misconfiguration is caught before first request
```

### Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `Jwt__Authority` | Keycloak realm URL | `http://keycloak:8080/realms/policymanagement` |
| `Jwt__Audience` | Keycloak client ID | `policymanagement-api` |
| `Jwt__RequireHttpsMetadata` | HTTPS enforcement | `false` (dev), `true` (prod) |

> **Security rule:** Never hardcode these values in `appsettings.json` or source code. Supply them via environment variables or a secrets manager. Never commit JWT secrets to source control.

---

## Section 6 — Error Responses for Auth Failures

Both `401` and `403` responses must be returned as RFC 7807 `ProblemDetails` with `Content-Type: application/problem+json`.

### How GlobalExceptionMiddleware Handles Auth Failures

ASP.NET Core's default challenge/forbid handling returns a bare `401`/`403` with no body. To return `ProblemDetails` for auth failures, override the JWT Bearer events in `Program.cs`:

```csharp
.AddJwtBearer(options =>
{
    // ... other options ...

    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            // Suppress the default 401 response
            context.HandleResponse();

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "A valid JWT Bearer token is required.",
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        },
        OnForbidden = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                Title = "Forbidden",
                Status = StatusCodes.Status403Forbidden,
                Detail = "You do not have permission to perform this action.",
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    };
});
```

### Status Code Mapping

| Status | Condition | Who returns it |
|--------|-----------|----------------|
| `401 Unauthorized` | Token absent, expired, invalid signature, wrong issuer or audience | JWT Bearer middleware (`OnChallenge` event) |
| `403 Forbidden` | Token valid and authenticated; user lacks required role (`Policy.Write`) | Authorization middleware (`OnForbidden` event) |

**Rules:**
- Stack traces are never exposed in `401` or `403` responses.
- The `correlationId` extension field is included in all `ProblemDetails` responses.
- `401` responses must include `WWW-Authenticate: Bearer` header (set automatically by JWT Bearer middleware).

---

## Section 7 — Integration Test Authentication

Integration tests use `WebApplicationFactory<Program>`. Tests must not depend on a running Keycloak instance. Override JWT Bearer to validate tokens signed with a known symmetric test key.

### Test Token Factory

Create `JwtTokenFactory` in the test project (`tests/PolicyManagement.API.Tests/Helpers/JwtTokenFactory.cs`):

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

internal static class JwtTokenFactory
{
    // Fixed symmetric key used in tests only — never used in production
    public const string TestSigningKey = "test-signing-key-must-be-at-least-32-chars!!";
    public const string TestIssuer = "test-issuer";
    public const string TestAudience = "policymanagement-api";

    public static string GenerateToken(
        string userId = "test-user-id",
        string email = "test@example.com",
        IEnumerable<string>? roles = null)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TestSigningKey));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new("email", email),
        };

        foreach (var role in roles ?? Enumerable.Empty<string>())
            claims.Add(new Claim("realm_access.roles", role));

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateExpiredToken(
        string userId = "test-user-id",
        string email = "test@example.com")
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TestSigningKey));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new("email", email),
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1),  // Expired 1 hour ago
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### Override JWT Bearer in WebApplicationFactory

```csharp
internal sealed class PolicyManagementApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Remove the production JWT Bearer handler
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IConfigureOptions<JwtBearerOptions>));
            if (descriptor != null) services.Remove(descriptor);

            // Replace with test JWT Bearer handler using symmetric key
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
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

Every endpoint integration test class must cover:

| Scenario | Token | Expected Status |
|----------|-------|-----------------|
| No token | None | `401 Unauthorized` |
| Expired token | Expired JWT | `401 Unauthorized` |
| Valid token (read user) | JWT, no `Policy.Write` role | `200 OK` on all `GET` endpoints |
| Valid token (read user) on flag endpoint | JWT, no `Policy.Write` role | `403 Forbidden` |
| Valid token with `Policy.Write` role | JWT with `Policy.Write` | `204 No Content` (success path) |

```csharp
[Fact]
public async Task FlagPolicies_WhenNoToken_Returns401()
{
    var client = _factory.CreateClient();
    // No Authorization header
    var response = await client.PatchAsJsonAsync("/api/v1/policies/flag", new { policyIds = new[] { Guid.NewGuid() } });
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}

[Fact]
public async Task FlagPolicies_WhenValidTokenWithoutWriteRole_Returns403()
{
    var client = _factory.CreateClient();
    var token = JwtTokenFactory.GenerateToken(roles: new[] { /* no Policy.Write */ });
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var response = await client.PatchAsJsonAsync("/api/v1/policies/flag", new { policyIds = new[] { Guid.NewGuid() } });
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task GetPolicies_WhenNoToken_Returns401()
{
    var client = _factory.CreateClient();
    var response = await client.GetAsync("/api/v1/policies");
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

---

## Section 8 — Anti-Patterns (What NOT to Do)

| Anti-pattern | Why it is wrong | Correct approach |
|---|---|---|
| Reading `HttpContext.User` directly in a handler | Couples Application layer to ASP.NET Core; breaks unit testability | Inject `ICurrentUserService` |
| `[AllowAnonymous]` on any policy endpoint | Bypasses authentication; exposes sensitive insurance data publicly | Remove it. All policy endpoints require auth. |
| Hardcoding JWT signing keys in source code | Security vulnerability — key exposure compromises all tokens | Supply via `Jwt__*` environment variables only |
| Disabling token validation (`ValidateIssuer = false`, etc.) | Allows forged tokens from any issuer | All four `Validate*` flags must be `true` in production |
| Cookie authentication | Designed for browser session state, not API token flows | Use JWT Bearer only |
| Auth logic in Domain layer | Violates Clean Architecture — Domain must have zero external dependencies | Auth stays in API layer; identity abstracted via `ICurrentUserService` in Application |
| Creating a custom token issuer in the BFF | The BFF is a validator, not an issuer; adding issuer logic violates SRP | Keycloak issues all tokens |
| Accessing `IHttpContextAccessor` outside the API layer | Couples Infrastructure/Application to ASP.NET Core HTTP pipeline | `IHttpContextAccessor` is used only in `CurrentUserService` in the API layer |
| Skipping `[ProducesResponseType]` for 401/403 | OpenAPI spec will be incomplete; contract-first requirement is violated | Every action must declare `401`; PATCH /flag must also declare `403` |
