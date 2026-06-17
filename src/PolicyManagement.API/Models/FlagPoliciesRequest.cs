namespace PolicyManagement.API.Models;

/// <summary>
/// Request body for <c>PATCH /api/v1/policies/flag</c>.
/// Maps directly to the <c>FlagPoliciesRequest</c> schema in the OpenAPI specification.
/// </summary>
/// <param name="PolicyIds">
/// The IDs of the policies to flag for review.
/// Must contain 1–100 unique, non-empty GUIDs.
/// Validated by <c>FlagPoliciesCommandValidator</c> before the handler executes.
/// </param>
public sealed record FlagPoliciesRequest(IReadOnlyList<Guid> PolicyIds);
