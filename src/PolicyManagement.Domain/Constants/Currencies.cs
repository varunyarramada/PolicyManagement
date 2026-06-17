namespace PolicyManagement.Domain.Constants;

/// <summary>
/// Defines the valid currency string constants accepted by the policy management system.
/// Covers the six currencies in use across Chubb APAC operations.
/// </summary>
public static class Currencies
{
    /// <summary>United States Dollar.</summary>
    public const string USD = "USD";

    /// <summary>Singapore Dollar.</summary>
    public const string SGD = "SGD";

    /// <summary>Hong Kong Dollar.</summary>
    public const string HKD = "HKD";

    /// <summary>Australian Dollar.</summary>
    public const string AUD = "AUD";

    /// <summary>Japanese Yen.</summary>
    public const string JPY = "JPY";

    /// <summary>Thai Baht.</summary>
    public const string THB = "THB";

    /// <summary>
    /// The complete set of valid currency codes. Comparison is case-insensitive.
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            USD,
            SGD,
            HKD,
            AUD,
            JPY,
            THB
        };

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="currency"/> is a recognised currency code.
    /// </summary>
    /// <param name="currency">The currency code to validate.</param>
    public static bool IsValid(string currency) => All.Contains(currency);
}
