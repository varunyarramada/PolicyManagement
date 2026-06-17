using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Domain.Models;

/// <summary>
/// Raw aggregation result returned by <see cref="Interfaces.IPolicyRepository.GetSummaryAsync"/>.
/// This is a pure data-carrier record with no presentation concerns.
/// The Application layer maps this to a <c>PolicySummaryResponse</c> DTO before returning it
/// to the controller.
/// </summary>
/// <param name="TotalPolicies">Total number of non-deleted policies.</param>
/// <param name="TotalPremium">
/// Aggregate sum of <c>PremiumAmount</c> across all non-deleted policies (all currencies combined).
/// Retained for informational use; consumers that need per-currency totals should use
/// <paramref name="PremiumByCurrency"/>.
/// </param>
/// <param name="FlaggedCount">Number of policies with <c>FlaggedForReview = true</c>.</param>
/// <param name="ExpiringSoonCount">
/// Number of Active policies whose <c>ExpiryDate</c> falls within the next 30 days.
/// </param>
/// <param name="CountByStatus">Breakdown of policy counts keyed by <see cref="PolicyStatus"/>.</param>
/// <param name="CountByLineOfBusiness">Breakdown of policy counts keyed by <see cref="LineOfBusiness"/>.</param>
/// <param name="CountByRegion">Breakdown of policy counts keyed by region string.</param>
/// <param name="PremiumByCurrency">
/// Sum of premium amounts grouped by ISO 4217 currency code string.
/// Only currencies with at least one policy are included, matching the
/// <c>premiumTotalByCurrency</c> field in the OpenAPI <c>PolicySummaryResponse</c> schema.
/// </param>
public sealed record PolicySummaryData(
    int TotalPolicies,
    decimal TotalPremium,
    int FlaggedCount,
    int ExpiringSoonCount,
    IReadOnlyDictionary<PolicyStatus, int> CountByStatus,
    IReadOnlyDictionary<LineOfBusiness, int> CountByLineOfBusiness,
    IReadOnlyDictionary<string, int> CountByRegion,
    IReadOnlyDictionary<string, decimal> PremiumByCurrency);
