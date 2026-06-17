namespace PolicyManagement.Infrastructure.Options;

/// <summary>
/// Strongly-typed configuration for cache TTL values.
/// Bound from the <c>"Cache"</c> section of <c>appsettings.json</c>.
/// Registered with <c>ValidateOnStart()</c> so misconfiguration fails at startup.
/// </summary>
/// <remarks>
/// Configuration key: <c>Cache</c>
/// <para>
/// Example <c>appsettings.json</c>:
/// <code>
/// "Cache": {
///   "PolicyTtlSeconds": 300,
///   "SummaryTtlSeconds": 60
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class CacheOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Cache";

    /// <summary>
    /// Gets or initialises the TTL in seconds for individual policy cache entries
    /// (cache key: <c>policy:v1:{policyId}</c>). Default: 300 seconds (5 minutes).
    /// </summary>
    public int PolicyTtlSeconds { get; init; } = 300;

    /// <summary>
    /// Gets or initialises the TTL in seconds for the summary statistics cache entry
    /// (cache key: <c>policy:v1:summary</c>). Default: 60 seconds (1 minute).
    /// </summary>
    public int SummaryTtlSeconds { get; init; } = 60;

    /// <summary>Gets the policy TTL as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan PolicyTtl => TimeSpan.FromSeconds(PolicyTtlSeconds);

    /// <summary>Gets the summary TTL as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SummaryTtl => TimeSpan.FromSeconds(SummaryTtlSeconds);
}
