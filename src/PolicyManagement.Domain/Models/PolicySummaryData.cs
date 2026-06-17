using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Domain.Models;

/// <summary>
/// Raw aggregation result returned by <see cref="Interfaces.IPolicyRepository.GetSummaryAsync"/>.
/// This is a pure data-carrier record with no presentation concerns.
/// The Application layer maps this to a <c>PolicySummaryResponse</c> DTO before returning it
/// to the controller.
/// </summary>
/// <param name="TotalPolicies">Total number of non-deleted policies.</param>
/// <param name="TotalPremium">Sum of <c>PremiumAmount</c> across all non-deleted policies.</param>
/// <param name="FlaggedCount">Number of policies with <c>FlaggedForReview = true</c>.</param>
/// <param name="ExpiringSoonCount">
/// Number of Active policies whose <c>ExpiryDate</c> falls within the next 30 days.
/// </param>
/// <param name="CountByStatus">Breakdown of policy counts keyed by <see cref="PolicyStatus"/>.</param>
/// <param name="CountByLineOfBusiness">Breakdown of policy counts keyed by <see cref="LineOfBusiness"/>.</param>
/// <param name="CountByRegion">Breakdown of policy counts keyed by region string.</param>
public sealed record PolicySummaryData(
    int TotalPolicies,
    decimal TotalPremium,
    int FlaggedCount,
    int ExpiringSoonCount,
    IReadOnlyDictionary<PolicyStatus, int> CountByStatus,
    IReadOnlyDictionary<LineOfBusiness, int> CountByLineOfBusiness,
    IReadOnlyDictionary<string, int> CountByRegion);
