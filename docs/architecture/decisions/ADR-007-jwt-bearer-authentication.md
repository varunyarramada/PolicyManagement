# ADR-007: JWT Bearer Authentication with Keycloak

- **Date:** 2026-06-17
- **Status:** Accepted

## Context

The PolicyManagement BFF exposes four API endpoints that manage insurance policy data for Chubb APAC across eight regions and four lines of business. The original assessment assumption **A-01** stated that no authentication or authorisation was required. That assumption has been superseded. All endpoints must now require a valid authenticated identity, and mutation endpoints must enforce role-based authorisation.

The BFF is a **Backend-for-Frontend** — its responsibility is to validate tokens, not to issue them. A frontend client authenticates separately with an identity provider, receives a JWT access token, and presents that token on every BFF request. The BFF validates the token and extracts claims to enforce access control. The BFF must not store user credentials, manage sessions, or issue its own tokens.

### Requirements driving this decision

- All four endpoints (`GET /policies`, `GET /policies/{id}`, `GET /policies/summary`, `PATCH /policies/flag`) require a valid JWT Bearer token — absent or invalid tokens return `401 Unauthorized`.
- The `PATCH /policies/flag` mutation endpoint requires an explicit role claim — missing role returns `403 Forbidden`.
- Read endpoints (`GET`) require authentication but no specific role beyond a valid token.
- The identity provider must be free, self-hosted, and production-grade with no usage-based licensing.
- Token validation must follow standard OAuth2/OIDC — no custom token schemes or shared-secret API keys.
- The BFF must not store user credentials or issue tokens.
- Auth configuration (issuer URL, audience, secrets) must be externalised — nothing hardcoded.

### Clean Architecture constraint

Authentication is an infrastructure concern in the API delivery layer. Domain and Application layers must remain free of auth concepts. Handlers that need user identity context must not access `HttpContext.User` directly — they receive identity through an abstraction (`ICurrentUserService`) defined in the `Application` layer and implemented in `Infrastructure` (or `API`).

## Decision

Use **ASP.NET Core built-in JWT Bearer authentication middleware** (`Microsoft.AspNetCore.Authentication.JwtBearer`) with **Keycloak** as the external self-hosted identity provider.

### Authentication flow

```
Frontend                 Keycloak                    PolicyManagement BFF
   │                        │                                │
   │──── Login ────────────▶│                                │
   │◀─── JWT access token ──│                                │
   │                        │                                │
   │──── GET /api/v1/policies ──────────────────────────────▶│
   │     Authorization: Bearer {token}                       │
   │                                       Validate token ──▶│ (signature, issuer,
   │                                       against Keycloak  │  audience, expiry)
   │◀──────────────────────────── 200 OK / 401 / 403 ────────│
```

1. Frontend authenticates with Keycloak and receives a signed JWT access token.
2. Frontend attaches the token as `Authorization: Bearer {token}` on every BFF request.
3. ASP.NET Core JWT Bearer middleware validates the token signature, issuer (`Authority`), audience, and expiry on every inbound request.
4. On success, claims (`sub`, `roles`, `email`) are populated into `HttpContext.User`.
5. ASP.NET Core authorization middleware evaluates `[Authorize]` and `[Authorize(Policy = "...")]` attributes.
6. Handlers that need user identity inject `ICurrentUserService` — they never read `HttpContext.User` directly.

### Role definitions

| Role | Scope | Endpoints |
|------|-------|-----------|
| `Policy.Read` | Read access to all policy data | `GET /api/v1/policies`, `GET /api/v1/policies/{id}`, `GET /api/v1/policies/summary` |
| `Policy.Write` | Write access — flag policies for review | `PATCH /api/v1/policies/flag` |

Any authenticated user with a valid token implicitly satisfies `Policy.Read`. `Policy.Write` is an explicit role check enforced via an ASP.NET Core named policy.

### Configuration

All auth configuration is bound to a strongly-typed `JwtOptions` class registered via `IOptions<JwtOptions>` and validated at startup with `ValidateOnStart()`:

```csharp
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Authority { get; init; } = string.Empty;  // Keycloak realm URL

    [Required]
    public string Audience { get; init; } = string.Empty;   // Keycloak client ID

    public bool RequireHttpsMetadata { get; init; } = true; // false in development only
}
```

Values are supplied via environment variables:

```
Jwt__Authority=http://keycloak:8080/realms/policymanagement
Jwt__Audience=policymanagement-api
Jwt__RequireHttpsMetadata=false   # development only; true in production
```

No values are hardcoded in source. No secrets are committed to source control.

### Keycloak setup

| Setting | Value |
|---------|-------|
| Deployment | Self-hosted Docker container alongside SQL Server in `docker-compose.yml` |
| Realm | `policymanagement` |
| Client | `policymanagement-api` (confidential client) |
| Client roles | `Policy.Read`, `Policy.Write` |
| Token format | JWT signed with RS256 |
| OIDC discovery | `{Authority}/.well-known/openid-configuration` (used by JWT Bearer middleware for automatic key rotation) |

### Application layer abstraction

`ICurrentUserService` is defined in `PolicyManagement.Application` and implemented in `PolicyManagement.API` (or `PolicyManagement.Infrastructure`). Handlers inject the interface — they never reference `HttpContext` or `ClaimsPrincipal` directly.

```csharp
// Application layer — no ASP.NET Core dependency
public interface ICurrentUserService
{
    string UserId { get; }
    string Email { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsInRole(string role);
}
```

This preserves the Clean Architecture rule: `Application` depends on `Domain` only. ASP.NET Core types (`HttpContext`, `ClaimsPrincipal`) remain entirely within the `API` layer.

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|--------------|
| **API Key authentication** | Custom `X-Api-Key` header validated against a known key in configuration. | Not suitable for user-level authorisation. No standard token format, no role claims, no expiry. Does not satisfy OAuth2/OIDC requirement. Rejected. |
| **Auth0 / Azure AD B2C** | Managed cloud identity providers with OAuth2/OIDC support. | Free tiers have monthly active user limits that are not suitable for production enterprise workloads. Vendor lock-in — migration later is costly. Not self-hosted. Rejected. |
| **Duende IdentityServer** | .NET-native OAuth2/OIDC server library embeddable inside the BFF process. | Requires a commercial licence for production use (BSL licence since v6). Free only for community/development use. Rejected on licensing grounds. |
| **ASP.NET Core Identity (local users)** | User accounts stored in the same SQL Server database. Forms or JWT-based login baked into the BFF. | The BFF would then be responsible for issuing tokens, managing sessions, and storing credentials — all concerns outside a BFF's responsibility. Not suitable for SSO or multi-app scenarios. Rejected. |
| **No authentication (original A-01)** | All endpoints publicly accessible without credentials. | Superseded. Insurance policy data (policyholders, premium amounts, underwriters) constitutes sensitive financial data that requires access control. Original assumption was an assessment simplification only. Rejected. |

## Consequences

### Positive

- **Zero licensing cost.** Keycloak is Apache 2.0 licensed. `Microsoft.AspNetCore.Authentication.JwtBearer` is MIT licensed and ships with the .NET SDK. No per-user or per-request fees.
- **Production-grade OAuth2/OIDC.** Keycloak is used in production by large financial and government organisations. It supports RS256 JWT signing, automatic key rotation via JWKS endpoint, MFA, SSO, and fine-grained role management.
- **Standard token validation.** JWT Bearer middleware automatically fetches Keycloak's JWKS endpoint for public key rotation. No manual key management in application code.
- **Clean Architecture preserved.** `ICurrentUserService` abstraction keeps all ASP.NET Core auth types (`ClaimsPrincipal`, `HttpContext`) out of `Domain` and `Application`. Handlers remain fully unit-testable without an HTTP context.
- **Local developer experience.** `docker-compose up` starts Keycloak alongside SQL Server. No external dependency on a shared cloud IdP.
- **Swappable identity provider.** Replacing Keycloak with Azure AD or another OIDC-compliant provider requires only a change to `JwtOptions.Authority` — no code changes.

### Negative / Trade-offs

- **Operational overhead.** Keycloak is an additional service to deploy, configure, monitor, and patch. It must be included in the production deployment and kept updated for security fixes.
- **Local setup complexity.** Developers must have Docker running to start the full stack. A Keycloak realm import script is required to provision the realm, client, and roles automatically on first `docker-compose up`.
- **Integration test complexity.** Integration tests using `WebApplicationFactory<Program>` require a mechanism to inject a pre-signed test JWT. A test token factory helper is required in the test project to generate tokens signed with a test key and configured via `WebApplicationFactory` overrides.
- **Keycloak cold start.** Keycloak takes longer to start than SQL Server. `docker-compose` health check dependencies must be configured so the BFF waits for Keycloak to be ready before accepting traffic.

## Compliance with Clean Architecture

This decision is compliant with the dependency rule (`API → Application → Domain ← Infrastructure`):

- JWT Bearer middleware registration lives in `PolicyManagement.API` (`Program.cs`).
- `JwtOptions` configuration class lives in `PolicyManagement.API` or a shared `PolicyManagement.Infrastructure` configuration namespace.
- `ICurrentUserService` interface lives in `PolicyManagement.Application/Interfaces/`.
- `CurrentUserService` implementation lives in `PolicyManagement.API` (accesses `IHttpContextAccessor`) or `PolicyManagement.Infrastructure`.
- `Domain` has zero knowledge of authentication concepts.
- `Application` handlers reference `ICurrentUserService` only — never `ClaimsPrincipal` or `HttpContext`.
