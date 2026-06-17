using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Application.Mappings;
using PolicyManagement.Application.Options;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicySummary;

/// <summary>
/// Handles <see cref="GetPolicySummaryQuery"/> and returns a <see cref="PolicySummaryResponse"/>.
/// <para>
/// Implements the <strong>cache-aside</strong> pattern:
/// <list type="number">
///   <item><description>Check the cache for key <c>policy:v1:summary</c>.</description></item>
///   <item><description>On a cache hit, return the cached <see cref="PolicySummaryResponse"/> immediately — the repository is not called.</description></item>
///   <item><description>On a cache miss, call <see cref="IPolicyRepository.GetSummaryAsync"/>.</description></item>
///   <item><description>Map the <c>PolicySummaryData</c> domain model to <see cref="PolicySummaryResponse"/> via <c>PolicyMappingExtensions.ToPolicySummaryResponse()</c>.</description></item>
///   <item><description>Write the DTO to the cache with <c>CacheOptions.SummaryTtl</c> and return it.</description></item>
/// </list>
/// </para>
/// <para>
/// TTL is sourced from <see cref="CacheOptions.SummaryTtl"/> (bound from the <c>Cache</c>
/// configuration section) — never hardcoded. See ADR-004.
/// </para>
/// <para>
/// The cache entry is invalidated by <c>FlagPoliciesCommandHandler</c> after a successful
/// commit so that summary statistics reflect flag changes within the next TTL window.
/// </para>
/// </summary>
public sealed class GetPolicySummaryQueryHandler(
    IPolicyRepository repository,
    ICacheService cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<GetPolicySummaryQueryHandler> logger)
    : IRequestHandler<GetPolicySummaryQuery, PolicySummaryResponse>
{
    private readonly CacheOptions _cacheOptions = cacheOptions.Value;

    /// <summary>
    /// The fixed cache key for the summary statistics entry.
    /// Must match the documented convention in ADR-004: <c>policy:v1:summary</c>.
    /// </summary>
    private const string CacheKey = "policy:v1:summary";

    /// <inheritdoc/>
    public async Task<PolicySummaryResponse> Handle(
        GetPolicySummaryQuery query,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {Query}", nameof(GetPolicySummaryQuery));

        var cached = await cache.GetAsync<PolicySummaryResponse>(CacheKey, cancellationToken);
        if (cached is not null)
        {
            logger.LogInformation(
                "{Query} — cache hit for key '{CacheKey}'",
                nameof(GetPolicySummaryQuery),
                CacheKey);

            return cached;
        }

        logger.LogInformation(
            "{Query} — cache miss for key '{CacheKey}', querying repository",
            nameof(GetPolicySummaryQuery),
            CacheKey);

        var data = await repository.GetSummaryAsync(cancellationToken);
        var response = data.ToPolicySummaryResponse();

        await cache.SetAsync(CacheKey, response, _cacheOptions.SummaryTtl, cancellationToken);

        logger.LogInformation(
            "{Query} completed — TotalCount={TotalCount}, FlaggedCount={FlaggedCount}, ExpiringSoonCount={ExpiringSoonCount}",
            nameof(GetPolicySummaryQuery),
            response.TotalCount,
            response.FlaggedCount,
            response.ExpiringSoonCount);

        return response;
    }
}
