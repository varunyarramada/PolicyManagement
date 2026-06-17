namespace PolicyManagement.Domain.Constants;

/// <summary>
/// Defines the valid sort field string constants for the policy list query.
/// These values correspond to the camelCase field names exposed in the OpenAPI spec
/// (<c>GET /api/v1/policies?sort=fieldName,direction</c>).
/// The Application layer <c>GetPoliciesQueryValidator</c> uses <see cref="All"/> to
/// validate the <c>sort</c> query parameter without duplicating magic strings.
/// </summary>
public static class PolicySortFields
{
    /// <summary>Sort by policy number.</summary>
    public const string PolicyNumber = "policyNumber";

    /// <summary>Sort by policyholder name.</summary>
    public const string PolicyholderName = "policyholderName";

    /// <summary>Sort by policy status.</summary>
    public const string Status = "status";

    /// <summary>Sort by line of business.</summary>
    public const string LineOfBusiness = "lineOfBusiness";

    /// <summary>Sort by region.</summary>
    public const string Region = "region";

    /// <summary>Sort by premium amount.</summary>
    public const string PremiumAmount = "premiumAmount";

    /// <summary>Sort by currency.</summary>
    public const string Currency = "currency";

    /// <summary>Sort by effective date.</summary>
    public const string EffectiveDate = "effectiveDate";

    /// <summary>Sort by expiry date.</summary>
    public const string ExpiryDate = "expiryDate";

    /// <summary>Sort by underwriter name.</summary>
    public const string Underwriter = "underwriter";

    /// <summary>Sort by creation timestamp (default sort field).</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>Sort by last-updated timestamp.</summary>
    public const string UpdatedAt = "updatedAt";

    /// <summary>
    /// The complete set of valid sort field strings. Comparison is case-insensitive.
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PolicyNumber,
            PolicyholderName,
            Status,
            LineOfBusiness,
            Region,
            PremiumAmount,
            Currency,
            EffectiveDate,
            ExpiryDate,
            Underwriter,
            CreatedAt,
            UpdatedAt
        };

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="field"/> is a recognised sort field.
    /// </summary>
    /// <param name="field">The sort field string to validate.</param>
    public static bool IsValid(string field) => All.Contains(field);
}
