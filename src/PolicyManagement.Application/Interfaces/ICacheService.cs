namespace PolicyManagement.Application.Interfaces;

/// <summary>
/// Abstraction over the caching layer. Handlers depend only on this interface — never on
/// <c>IMemoryCache</c>, <c>IDistributedCache</c>, or any concrete cache technology.
/// The in-memory implementation (<c>InMemoryCacheService</c>) lives in
/// <c>PolicyManagement.Infrastructure/Caching/</c> and can be swapped for a Redis-backed
/// implementation without any change to handler code.
/// </summary>
/// <remarks>
/// Cache key conventions (see ADR-004):
/// <list type="table">
///   <listheader><term>Endpoint</term><description>Cache key / TTL</description></listheader>
///   <item>
///     <term>GET /api/v1/policies/{id}</term>
///     <description><c>policy:v1:{policyId}</c> — TTL from <c>CacheOptions.PolicyTtl</c></description>
///   </item>
///   <item>
///     <term>GET /api/v1/policies/summary</term>
///     <description><c>policy:v1:summary</c> — TTL from <c>CacheOptions.SummaryTtl</c></description>
///   </item>
/// </list>
/// The list endpoint (<c>GET /api/v1/policies</c>) is <strong>never cached</strong>.
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// Retrieves a cached value by key. Returns <see langword="null"/> on a cache miss.
    /// </summary>
    /// <typeparam name="T">The expected type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>
    /// The deserialised cached value, or <see langword="null"/> if the key does not exist
    /// or has expired.
    /// </returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Stores a value in the cache under the specified key with the given TTL.
    /// Any existing value for the key is overwritten.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache. Must be JSON-serialisable.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">Time-to-live for the cache entry.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Removes the cache entry with the specified key. No-ops if the key does not exist.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
