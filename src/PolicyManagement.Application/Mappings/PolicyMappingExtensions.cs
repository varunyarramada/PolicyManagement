using PolicyManagement.Application.DTOs;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Models;

namespace PolicyManagement.Application.Mappings;

/// <summary>
/// Static extension methods for mapping Domain types to Application DTOs.
/// No AutoMapper — all mappings are explicit, compile-time-safe, and easy to trace.
/// </summary>
public static class PolicyMappingExtensions
{
    /// <summary>
    /// Maps a <see cref="Policy"/> entity to a <see cref="PolicyDto"/>.
    /// The <c>LineOfBusiness</c> enum is serialised using
    /// <see cref="ToLineOfBusinessString"/> to ensure <c>AH</c> → <c>"A&amp;H"</c>.
    /// </summary>
    /// <param name="policy">The source domain entity.</param>
    /// <returns>An immutable <see cref="PolicyDto"/> record.</returns>
    public static PolicyDto ToDto(this Policy policy) =>
        new(
            policy.Id,
            policy.PolicyNumber,
            policy.PolicyholderName,
            policy.LineOfBusiness.ToLineOfBusinessString(),
            policy.Status.ToString(),
            policy.PremiumAmount,
            policy.Currency,
            policy.EffectiveDate,
            policy.ExpiryDate,
            policy.Region,
            policy.Underwriter,
            policy.FlaggedForReview,
            policy.CreatedAt,
            policy.UpdatedAt);

    /// <summary>
    /// Maps a <see cref="PolicySummaryData"/> domain model to a <see cref="PolicySummaryResponse"/> DTO.
    /// Dictionary keys are converted to their display-string equivalents so enum keys become
    /// the API-facing strings (e.g. <c>LineOfBusiness.AH</c> → <c>"A&amp;H"</c>).
    /// </summary>
    /// <param name="data">The raw aggregation result from the repository.</param>
    /// <returns>An immutable <see cref="PolicySummaryResponse"/> record.</returns>
    public static PolicySummaryResponse ToPolicySummaryResponse(this PolicySummaryData data) =>
        new(
            TotalCount: data.TotalPolicies,
            FlaggedCount: data.FlaggedCount,
            ExpiringSoonCount: data.ExpiringSoonCount,
            CountByStatus: data.CountByStatus
                .ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value),
            CountByRegion: data.CountByRegion
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value),
            CountByLineOfBusiness: data.CountByLineOfBusiness
                .ToDictionary(
                    kvp => kvp.Key.ToLineOfBusinessString(),
                    kvp => kvp.Value),
            PremiumTotalByCurrency: data.PremiumByCurrency
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value));

    /// <summary>
    /// Converts a <see cref="LineOfBusiness"/> enum value to its API display string.
    /// <c>AH</c> is serialised as <c>"A&amp;H"</c> to match the OpenAPI contract and
    /// database schema. All other members use the default <see cref="Enum.ToString()"/> result.
    /// </summary>
    /// <param name="lob">The <see cref="LineOfBusiness"/> value to convert.</param>
    /// <returns>The display string representation.</returns>
    public static string ToLineOfBusinessString(this LineOfBusiness lob) =>
        lob == LineOfBusiness.AH ? "A&H" : lob.ToString();
}
