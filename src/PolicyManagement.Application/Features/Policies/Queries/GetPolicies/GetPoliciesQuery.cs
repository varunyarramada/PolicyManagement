using MediatR;
using PolicyManagement.Application.DTOs;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicies;

/// <summary>
/// Query for the paginated policy list endpoint (<c>GET /api/v1/policies</c>).
/// All parameters are optional and default to the OpenAPI-specified values.
/// The list endpoint is <strong>never cached</strong> — each request hits the repository.
/// </summary>
/// <param name="Page">1-based page number. Must be ≥ 1. Default: 1.</param>
/// <param name="Size">Number of records per page. Must be 1–100. Default: 20.</param>
/// <param name="Sort">
/// Sort expression as <c>fieldName,direction</c> (e.g. <c>premiumAmount,desc</c>).
/// The direction part is optional and defaults to <c>desc</c> when absent.
/// Allowed field names: see <see cref="Domain.Constants.PolicySortFields"/>.
/// Default: <c>createdAt,desc</c>.
/// </param>
/// <param name="Status">Optional filter by policy status string (e.g. <c>"Active"</c>).</param>
/// <param name="LineOfBusiness">Optional filter by line of business string (e.g. <c>"A&amp;H"</c>).</param>
/// <param name="Region">Optional filter by APAC region string (e.g. <c>"Singapore"</c>).</param>
/// <param name="EffectiveDateFrom">Optional inclusive start of effective date range.</param>
/// <param name="EffectiveDateTo">Optional inclusive end of effective date range.</param>
/// <param name="Search">
/// Optional case-insensitive substring search across policy number,
/// policyholder name, and underwriter name.
/// </param>
public sealed record GetPoliciesQuery(
    int Page = 1,
    int Size = 20,
    string Sort = "createdAt,desc",
    string? Status = null,
    string? LineOfBusiness = null,
    string? Region = null,
    DateOnly? EffectiveDateFrom = null,
    DateOnly? EffectiveDateTo = null,
    string? Search = null) : IRequest<PagedPolicyResponse>;
