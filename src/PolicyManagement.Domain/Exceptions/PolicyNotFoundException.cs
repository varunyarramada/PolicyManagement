namespace PolicyManagement.Domain.Exceptions;

/// <summary>
/// Thrown when a requested policy cannot be found in the data store.
/// Maps to HTTP 404 Not Found in the API layer.
/// </summary>
public sealed class PolicyNotFoundException : DomainException
{
    /// <summary>
    /// Gets the ID of the policy that was not found.
    /// </summary>
    public Guid PolicyId { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="PolicyNotFoundException"/> for the specified policy ID.
    /// </summary>
    /// <param name="policyId">The ID of the policy that could not be found.</param>
    public PolicyNotFoundException(Guid policyId)
        : base($"Policy with ID '{policyId}' was not found.")
    {
        PolicyId = policyId;
    }
}
