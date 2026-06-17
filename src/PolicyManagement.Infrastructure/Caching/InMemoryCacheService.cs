using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Infrastructure.Options;
using System.Text.Json;

namespace PolicyManagement.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of <see cref="ICacheService"/> backed by
/// <see cref="IMemoryCache"/>.
/// <para>
/// Registered as <c>Singleton</c> — the underlying <c>IMemoryCache</c> is itself
/// a singleton managed by the DI container.
/// </para>
/// <para>
/// To swap to Redis, implement a <c>RedisCacheService : ICacheService</c> class
/// in this folder and change the DI registration in
/// <c>InfrastructureServiceExtensions</c> — no handler code changes required (ADR-004).
/// </para>
/// </summary>
public sealed class InMemoryCacheService(
    IMemoryCache cache,
    IOptions<CacheOptions> options)
    : ICacheService
{
    private readonly CacheOptions _options = options.Value;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        if (cache.TryGetValue(key, out var raw) && raw is string json)
        {
            var value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return Task.FromResult(value);
        }

        return Task.FromResult<T?>(null);
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);

        cache.Set(key, json, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}
