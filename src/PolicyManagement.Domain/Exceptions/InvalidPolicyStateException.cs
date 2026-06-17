namespace PolicyManagement.Domain.Exceptions;

/// <summary>
/// Thrown when an operation cannot be performed because the policy is in an invalid state
/// for that operation. Maps to HTTP 409 Conflict in the API layer.
/// </summary>
public sealed class InvalidPolicyStateException : DomainException
{
    /// <summary>
    /// Gets the ID of the policy that is in an invalid state.
    /// </summary>
    public Guid PolicyId { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="InvalidPolicyStateException"/> with the
    /// specified policy ID and reason.
    /// </summary>
    /// <param name="policyId">The ID of the policy in the invalid state.</param>
    /// <param name="reason">A description of why the current state is invalid for the attempted operation.</param>
    public InvalidPolicyStateException(Guid policyId, string reason)
        : base($"Policy '{policyId}' is in an invalid state for the requested operation: {reason}")
    {
        PolicyId = policyId;
    }
}
