using MediatR;

namespace PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;

/// <summary>
/// Command that flags one or more policies for review in a single atomic operation.
/// Maps to the <c>PATCH /api/v1/policies/flag</c> endpoint.
/// </summary>
/// <remarks>
/// Requires the <c>Policy.Write</c> role — enforced at the controller level via
/// <c>[Authorize(Policy = "PolicyWrite")]</c> before MediatR dispatches the command.
/// <para>
/// The handler performs the following steps:
/// <list type="number">
///   <item><description>Verify all supplied policy IDs exist — throws <c>PolicyNotFoundException</c> on the first missing ID.</description></item>
///   <item><description>Verify no supplied policy is already flagged — throws <c>InvalidPolicyStateException</c> on the first already-flagged policy.</description></item>
///   <item><description>Call <c>Policy.Flag()</c> on each entity and persist via <c>IPolicyRepository.UpdateRangeAsync</c> in a single atomic save.</description></item>
///   <item><description>Publish one <c>PolicyFlaggedEvent</c> per flagged policy after a successful commit.</description></item>
///   <item><description>Invalidate the summary cache (<c>policy:v1:summary</c>) and each per-policy cache entry (<c>policy:v1:{id}</c>) after commit.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <param name="PolicyIds">
/// The IDs of the policies to flag. Must contain 1–100 unique, non-empty GUIDs.
/// Validated by <c>FlagPoliciesCommandValidator</c>.
/// </param>
public sealed record FlagPoliciesCommand(IReadOnlyList<Guid> PolicyIds) : IRequest;
