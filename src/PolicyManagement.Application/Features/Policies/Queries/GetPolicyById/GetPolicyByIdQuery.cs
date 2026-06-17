using MediatR;
using PolicyManagement.Application.DTOs;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicyById;

/// <summary>
/// Query for the single-policy detail endpoint (<c>GET /api/v1/policies/{id}</c>).
/// Returns the full <see cref="PolicyDto"/> for the requested policy ID.
/// <para>
/// The response is cached under the key <c>policy:v1:{id}</c> with a TTL sourced from
/// <c>CacheOptions.PolicyTtl</c> (default 5 minutes). See ADR-004.
/// </para>
/// </summary>
/// <param name="Id">The unique identifier of the policy to retrieve.</param>
public sealed record GetPolicyByIdQuery(Guid Id) : IRequest<PolicyDto>;
