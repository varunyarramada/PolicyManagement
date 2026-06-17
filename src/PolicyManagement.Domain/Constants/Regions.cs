namespace PolicyManagement.Domain.Constants;

/// <summary>
/// Defines the valid APAC region string constants for policy management.
/// Regions are stored as plain strings rather than an enum because some region names
/// (e.g. "Hong Kong") contain spaces, making enum-to-string conversion fragile.
/// </summary>
public static class Regions
{
    /// <summary>Singapore region.</summary>
    public const string Singapore = "Singapore";

    /// <summary>Hong Kong region.</summary>
    public const string HongKong = "Hong Kong";

    /// <summary>Australia region.</summary>
    public const string Australia = "Australia";

    /// <summary>Japan region.</summary>
    public const string Japan = "Japan";

    /// <summary>Thailand region.</summary>
    public const string Thailand = "Thailand";

    /// <summary>Indonesia region.</summary>
    public const string Indonesia = "Indonesia";

    /// <summary>Malaysia region.</summary>
    public const string Malaysia = "Malaysia";

    /// <summary>Philippines region.</summary>
    public const string Philippines = "Philippines";

    /// <summary>
    /// The complete set of valid region strings. Comparison is case-insensitive.
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Singapore,
            HongKong,
            Australia,
            Japan,
            Thailand,
            Indonesia,
            Malaysia,
            Philippines
        };

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="region"/> is a recognised APAC region.
    /// </summary>
    /// <param name="region">The region string to validate.</param>
    public static bool IsValid(string region) => All.Contains(region);
}
