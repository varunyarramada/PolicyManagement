using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Application.Mappings;
using PolicyManagement.Application.Options;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicyById;

/// <summary>
/// Handles <see cref="GetPolicyByIdQuery"/> and returns a <see cref="PolicyDto"/>.
/// <para>
/// Implements the <strong>cache-aside</strong> pattern:
/// <list type="number">
///   <item><description>Check the cache for key <c>policy:v1:{id}</c>.</description></item>
///   <item><description>On a cache hit, return the cached <see cref="PolicyDto"/> immediately — the repository is not called.</description></item>
///   <item><description>On a cache miss, call <see cref="IPolicyRepository.GetByIdAsync"/>.</description></item>
///   <item><description>If the policy does not exist, throw <see cref="PolicyNotFoundException"/> (mapped to <c>404 Not Found</c> by <c>GlobalExceptionMiddleware</c>).</description></item>
///   <item><description>Map the entity to <see cref="PolicyDto"/>, write it to the cache with <c>CacheOptions.PolicyTtl</c>, and return it.</description></item>
/// </list>
/// </para>
/// <para>
/// TTL is sourced from <see cref="CacheOptions.PolicyTtl"/> (bound from the <c>Cache</c>
/// configuration section) — never hardcoded. See ADR-004.
/// </para>
/// </summary>
public sealed class GetPolicyByIdQueryHandler(
    IPolicyRepository repository,
    ICacheService cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<GetPolicyByIdQueryHandler> logger)
    : IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{
    private readonly CacheOptions _cacheOptions = cacheOptions.Value;

    /// <summary>
    /// Cache key format for individual policy entries.
    /// Must match the documented convention in <see cref="ICacheService"/> (ADR-004).
    /// </summary>
    private static string CacheKey(Guid id) => $"policy:v1:{id}";

    /// <inheritdoc/>
    public async Task<PolicyDto> Handle(
        GetPolicyByIdQuery query,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(query.Id);

        // ---- Cache-aside: check cache first ----
        var cached = await cache.GetAsync<PolicyDto>(key, cancellationToken);
        if (cached is not null)
        {
            logger.LogInformation(
                "Cache hit for {Query} — key {CacheKey}",
                nameof(GetPolicyByIdQuery), key);

            return cached;
        }

        logger.LogInformation(
            "Cache miss for {Query} — key {CacheKey}, querying repository",
            nameof(GetPolicyByIdQuery), key);

        // ---- Repository lookup ----
        var policy = await repository.GetByIdAsync(query.Id, cancellationToken);

        if (policy is null)
        {
            logger.LogWarning(
                "{Query} — policy {PolicyId} not found",
                nameof(GetPolicyByIdQuery), query.Id);

            throw new PolicyNotFoundException(query.Id);
        }

        // ---- Map and populate cache ----
        var dto = policy.ToDto();

        await cache.SetAsync(key, dto, _cacheOptions.PolicyTtl, cancellationToken);

        logger.LogInformation(
            "{Query} — policy {PolicyId} retrieved and cached (TTL {Ttl}s)",
            nameof(GetPolicyByIdQuery), query.Id, _cacheOptions.PolicyTtlSeconds);

        return dto;
    }
}
