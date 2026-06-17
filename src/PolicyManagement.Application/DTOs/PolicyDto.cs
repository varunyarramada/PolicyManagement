namespace PolicyManagement.Application.DTOs;

/// <summary>
/// Represents a single insurance policy as returned by the API.
/// Used in both list responses (<c>GET /api/v1/policies</c>) and
/// single-item responses (<c>GET /api/v1/policies/{id}</c>).
/// Property names use camelCase serialisation to match the OpenAPI contract.
/// </summary>
/// <param name="Id">Unique identifier of the policy (client-generated GUID).</param>
/// <param name="PolicyNumber">Unique policy number in format <c>POL-XXXXXX</c>.</param>
/// <param name="PolicyholderName">Full name of the policyholder.</param>
/// <param name="LineOfBusiness">Line of business as a string (e.g. <c>"A&amp;H"</c>, <c>"Marine"</c>).</param>
/// <param name="Status">Policy lifecycle status as a string (e.g. <c>"Active"</c>, <c>"Expired"</c>).</param>
/// <param name="PremiumAmount">The premium amount. Precision: 18 digits, 2 decimal places.</param>
/// <param name="Currency">ISO 4217 currency code (e.g. <c>"USD"</c>, <c>"SGD"</c>).</param>
/// <param name="EffectiveDate">Date the policy becomes effective in ISO 8601 format <c>YYYY-MM-DD</c>.</param>
/// <param name="ExpiryDate">Date the policy expires in ISO 8601 format <c>YYYY-MM-DD</c>.</param>
/// <param name="Region">APAC region name (e.g. <c>"Singapore"</c>, <c>"Hong Kong"</c>).</param>
/// <param name="Underwriter">Name of the underwriter responsible for this policy.</param>
/// <param name="FlaggedForReview">Indicates whether the policy has been flagged for review.</param>
/// <param name="CreatedAt">UTC-aware timestamp at which the policy record was created.</param>
/// <param name="UpdatedAt">UTC-aware timestamp at which the policy record was last updated.</param>
public sealed record PolicyDto(
    Guid Id,
    string PolicyNumber,
    string PolicyholderName,
    string LineOfBusiness,
    string Status,
    decimal PremiumAmount,
    string Currency,
    DateOnly EffectiveDate,
    DateOnly ExpiryDate,
    string Region,
    string Underwriter,
    bool FlaggedForReview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
