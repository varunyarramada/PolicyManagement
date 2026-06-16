# ADR-004: ICacheService Abstraction over Direct Redis or IMemoryCache

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The PolicyManagement BFF caches two responses to reduce database load:

- `GET /api/v1/policies/{id}` — individual policy responses, cached per policy ID.
- `GET /api/v1/policies/summary` — aggregated statistics, cached with a short TTL (1 minute) and invalidated when the `FlagPoliciesCommand` commits.

The caching concern is that the current implementation uses in-memory caching (suitable for a single-instance development deployment), but the production deployment is expected to move to a Redis-backed distributed cache so that multiple service instances share a consistent cache state. The question is how to introduce caching in a way that does not couple the Application layer to a specific caching technology.

Three approaches were considered:

1. **Direct `IMemoryCache` injection in handlers** — ASP.NET Core's `IMemoryCache` is injected into handlers directly.
2. **Direct `IDistributedCache` injection in handlers** — ASP.NET Core's `IDistributedCache` abstraction is injected into handlers. It supports both in-memory and Redis implementations.
3. **Custom `ICacheService` abstraction** — A purpose-built interface defined in `Application` that hides the caching mechanism behind a typed, domain-aware API.

## Decision

The service defines a **custom `ICacheService` interface in `PolicyManagement.Application/Interfaces/`**. The in-memory implementation (`InMemoryCacheService`) lives in `PolicyManagement.Infrastructure/Caching/`. Handlers depend only on `ICacheService`.

```
// Application/Interfaces/ICacheService.cs
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}
```

Cache key conventions:

| Data cached | Cache key | TTL |
|---|---|---|
| Single policy by ID | `policy:v1:{policyId}` | 5 minutes |
| Summary statistics | `policy:v1:summary` | 1 minute |

The summary cache key is invalidated by `FlagPoliciesCommandHandler` after a successful commit by calling `ICacheService.RemoveAsync("policy:v1:summary", ct)`.

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **Direct `IMemoryCache` injection** | Handlers inject `Microsoft.Extensions.Caching.Memory.IMemoryCache` directly. Available with no additional packages — it is part of the ASP.NET Core hosting model. | `IMemoryCache` is an ASP.NET Core type. Injecting it into `Application` handlers introduces a dependency on `Microsoft.Extensions.Caching.Memory` in the Application project, which is a framework-level concern. This violates the Clean Architecture rule: Application should not depend on infrastructure packages. Additionally, `IMemoryCache` is not distributed-cache-aware — replacing it with Redis requires touching every handler that uses it. |
| **Direct `IDistributedCache` injection** | Handlers inject `Microsoft.Extensions.Caching.Distributed.IDistributedCache`. This interface is already backed by both `MemoryDistributedCache` (in-memory) and `StackExchange.Redis` (Redis), so the swap from in-memory to Redis requires only a DI registration change. | `IDistributedCache` works only with `byte[]` or `string` payloads — it has no typed `Get<T>` method. Handlers would need to serialise and deserialise JSON themselves, adding boilerplate and potential serialisation inconsistency across handlers. It also introduces `Microsoft.Extensions.Caching.Abstractions` as a dependency of the Application project. A thin wrapper around `IDistributedCache` is a better approach — which is what `ICacheService` is. |
| **No caching (every request hits the database)** | No cache layer. All three read endpoints hit SQL Server on every request. | Acceptable for development and the seed dataset. At production scale, `GET /api/v1/policies/summary` runs GROUP BY aggregations across the full table on every request. Without caching, concurrent dashboard requests create unnecessary database load. The assessment lists caching as a bonus feature with an expectation of a clear invalidation strategy — no caching is explicitly not meeting that requirement. |

## Consequences

### Positive

- **Clean Architecture compliance.** `ICacheService` is defined in `Application`. It has no dependency on `Microsoft.Extensions.Caching.*` or any infrastructure package. Handlers depend on the interface. The concrete `InMemoryCacheService` lives in `Infrastructure` and is registered in `Program.cs`.
- **Redis swap path is a single DI registration change.** To replace `InMemoryCacheService` with a Redis-backed implementation, a new `RedisCacheService : ICacheService` class is added to `Infrastructure` and its registration in `Program.cs` is changed from `AddSingleton<ICacheService, InMemoryCacheService>` to `AddSingleton<ICacheService, RedisCacheService>`. No handler, validator, or domain type is touched.
- **Typed API hides serialisation.** `ICacheService.GetAsync<T>` and `SetAsync<T>` are generic. The implementation handles JSON serialisation internally. Handlers work with strongly-typed objects — they never see `byte[]` or `string` payloads.
- **Explicit invalidation.** The `RemoveAsync` method makes cache invalidation a first-class operation. The `FlagPoliciesCommandHandler` calls `RemoveAsync` for the summary key — this is testable via `Mock<ICacheService>` without needing a real cache.
- **Unit testability.** `Mock<ICacheService>` is trivial to set up. Cache hit paths and cache miss paths can be tested independently by controlling the mock's return values for `GetAsync`.

### Negative / Trade-offs

- **An additional interface for a small service.** A BFF with two cached endpoints does not strictly require a custom abstraction — `IDistributedCache` would work. The abstraction pays forward to the Redis swap, but introduces a thin layer of indirection that some developers consider unnecessary for the current scale.
- **In-memory cache is not distributed.** If multiple instances of the service run behind a load balancer, each instance maintains its own in-memory cache. Cache invalidation after a flag operation only invalidates the cache on the instance that handled the request. Other instances continue serving stale summary data until their TTL expires. This is explicitly acceptable for the development environment and is the documented motivation for the Redis swap path.
- **TTL selection is a heuristic.** The 1-minute TTL for the summary and 5-minute TTL for individual policies are estimates. TTL values that are too short reduce cache effectiveness; values that are too long increase stale data window. These values are externalised via `IOptions<CacheOptions>` so they can be tuned without code changes.

## Compliance with Clean Architecture

`ICacheService` is defined in `PolicyManagement.Application/Interfaces/` — a layer that depends only on `Domain`. Its implementation `InMemoryCacheService` lives in `PolicyManagement.Infrastructure/Caching/`. The `Application.csproj` has no reference to `Microsoft.Extensions.Caching.Memory` or any caching NuGet package. The interface is the only contract — this is fully consistent with the inward-dependency rule.
