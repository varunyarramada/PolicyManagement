using MediatR;
using PolicyManagement.Application.DTOs;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicySummary;

/// <summary>
/// Query that returns aggregated statistics across all non-deleted policies.
/// Maps to the <c>GET /api/v1/policies/summary</c> endpoint.
/// </summary>
/// <remarks>
/// The result is cached under the key <c>policy:v1:summary</c> with a TTL sourced
/// from <c>CacheOptions.SummaryTtl</c> — never hardcoded. See ADR-004.
/// The cache entry is invalidated by <c>FlagPoliciesCommandHandler</c> after a
/// successful commit so that flagged-count statistics remain accurate within the TTL window.
/// </remarks>
public sealed record GetPolicySummaryQuery : IRequest<PolicySummaryResponse>;
