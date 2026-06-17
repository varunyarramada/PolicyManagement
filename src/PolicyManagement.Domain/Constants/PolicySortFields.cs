namespace PolicyManagement.Domain.Constants;

/// <summary>
/// Defines sort field string constants for the policy list query.
/// </summary>
/// <remarks>
/// <para>
/// <strong>API-exposed sort fields</strong> (the <see cref="All"/> set): the seven fields
/// documented in the OpenAPI spec (<c>GET /api/v1/policies?sort=fieldName,direction</c>).
/// <c>GetPoliciesQueryValidator</c> uses <see cref="All"/> to validate the <c>sort</c>
/// query parameter. Any field <em>not</em> in <see cref="All"/> returns <c>400 Bad Request</c>.
/// </para>
/// <para>
/// <strong>Repository-only sort fields</strong> (constants defined here but excluded from
/// <see cref="All"/>): <c>lineOfBusiness</c>, <c>region</c>, <c>currency</c>,
/// <c>underwriter</c>, <c>updatedAt</c>. These are supported by
/// <c>PolicyRepository.ApplySort</c> but are not part of the public API contract.
/// They must not be added to <see cref="All"/> without a corresponding OpenAPI spec update.
/// </para>
/// </remarks>
public static class PolicySortFields
{
    /// <summary>Sort by policy number. Exposed in API.</summary>
    public const string PolicyNumber = "policyNumber";

    /// <summary>Sort by policyholder name. Exposed in API.</summary>
    public const string PolicyholderName = "policyholderName";

    /// <summary>Sort by policy status. Exposed in API.</summary>
    public const string Status = "status";

    /// <summary>
    /// Sort by line of business.
    /// <strong>Repository-only</strong> — not exposed in the public API sort contract.
    /// </summary>
    public const string LineOfBusiness = "lineOfBusiness";

    /// <summary>
    /// Sort by region.
    /// <strong>Repository-only</strong> — not exposed in the public API sort contract.
    /// </summary>
    public const string Region = "region";

    /// <summary>Sort by premium amount. Exposed in API.</summary>
    public const string PremiumAmount = "premiumAmount";

    /// <summary>
    /// Sort by currency.
    /// <strong>Repository-only</strong> — not exposed in the public API sort contract.
    /// </summary>
    public const string Currency = "currency";

    /// <summary>Sort by effective date. Exposed in API.</summary>
    public const string EffectiveDate = "effectiveDate";

    /// <summary>Sort by expiry date. Exposed in API.</summary>
    public const string ExpiryDate = "expiryDate";

    /// <summary>
    /// Sort by underwriter name.
    /// <strong>Repository-only</strong> — not exposed in the public API sort contract.
    /// </summary>
    public const string Underwriter = "underwriter";

    /// <summary>Sort by creation timestamp (default sort field). Exposed in API.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>
    /// Sort by last-updated timestamp.
    /// <strong>Repository-only</strong> — not exposed in the public API sort contract.
    /// </summary>
    public const string UpdatedAt = "updatedAt";

    /// <summary>
    /// The seven sort fields exposed in the OpenAPI spec for <c>GET /api/v1/policies</c>.
    /// Comparison is case-insensitive. Used by <c>GetPoliciesQueryValidator</c> to validate
    /// the <c>sort</c> query parameter — any value outside this set returns <c>400 Bad Request</c>.
    /// <para>
    /// Fields intentionally excluded: <c>lineOfBusiness</c>, <c>region</c>, <c>currency</c>,
    /// <c>underwriter</c>, <c>updatedAt</c>. These are supported by the repository but are
    /// not part of the public API contract.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PolicyNumber,
            PolicyholderName,
            Status,
            PremiumAmount,
            EffectiveDate,
            ExpiryDate,
            CreatedAt,
        };

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="field"/> is an API-exposed sort field
    /// (i.e., present in <see cref="All"/>).
    /// </summary>
    /// <param name="field">The sort field string to validate.</param>
    public static bool IsValid(string field) => All.Contains(field);
}

