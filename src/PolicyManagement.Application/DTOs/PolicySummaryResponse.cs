namespace PolicyManagement.Application.DTOs;

/// <summary>
/// Aggregated statistics returned by <c>GET /api/v1/policies/summary</c>.
/// Maps directly to the <c>PolicySummaryResponse</c> schema in the OpenAPI specification.
/// Produced by mapping <c>PolicySummaryData</c> (Domain model) in
/// <c>PolicyMappingExtensions.ToPolicySummaryResponse()</c>.
/// </summary>
/// <param name="TotalCount">Total number of non-deleted policies.</param>
/// <param name="FlaggedCount">Number of policies with <c>FlaggedForReview = true</c>.</param>
/// <param name="ExpiringSoonCount">
/// Number of <c>Active</c> policies whose expiry date falls within the next 30 calendar days.
/// </param>
/// <param name="CountByStatus">Policy count grouped by status string key.</param>
/// <param name="CountByRegion">Policy count grouped by APAC region string key.</param>
/// <param name="CountByLineOfBusiness">Policy count grouped by line-of-business string key.</param>
/// <param name="PremiumTotalByCurrency">
/// Sum of premium amounts grouped by currency code. Only currencies with at least one policy
/// are included.
/// </param>
public sealed record PolicySummaryResponse(
    int TotalCount,
    int FlaggedCount,
    int ExpiringSoonCount,
    IReadOnlyDictionary<string, int> CountByStatus,
    IReadOnlyDictionary<string, int> CountByRegion,
    IReadOnlyDictionary<string, int> CountByLineOfBusiness,
    IReadOnlyDictionary<string, decimal> PremiumTotalByCurrency);
