namespace PolicyManagement.Domain.Events;

/// <summary>
/// Domain event raised when a single policy is flagged for review.
/// <c>FlagPoliciesCommandHandler</c> publishes one instance of this event per flagged policy
/// after a successful database commit, enabling fine-grained downstream processing
/// (audit log, notifications) per policy without batching concerns.
/// Consumers subscribe via <c>IEventPublisher</c>.
/// </summary>
/// <param name="PolicyId">The ID of the policy that was flagged.</param>
/// <param name="FlaggedByUserId">The ID of the user who initiated the flag operation.</param>
/// <param name="FlaggedAt">The UTC timestamp at which the flag operation completed.</param>
public sealed record PolicyFlaggedEvent(
    Guid PolicyId,
    string FlaggedByUserId,
    DateTimeOffset FlaggedAt);
