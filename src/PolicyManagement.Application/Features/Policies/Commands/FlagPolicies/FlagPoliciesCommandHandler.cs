using MediatR;
using Microsoft.Extensions.Logging;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Domain.Events;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;

/// <summary>
/// Handles <see cref="FlagPoliciesCommand"/> — flags one or more policies for review
/// in a single atomic database operation.
/// </summary>
/// <remarks>
/// Execution sequence:
/// <list type="number">
///   <item><description>Load each policy individually via <see cref="IPolicyRepository.GetByIdAsync"/>. Throws <see cref="PolicyNotFoundException"/> on the first ID that cannot be found — no partial commits occur.</description></item>
///   <item><description>Validate state — throws <see cref="InvalidPolicyStateException"/> for the first policy that is already flagged.</description></item>
///   <item><description>Call <see cref="Domain.Entities.Policy.Flag"/> on each entity and persist all changes in a single call to <see cref="IPolicyRepository.UpdateRangeAsync"/>.</description></item>
///   <item><description>Publish one <see cref="PolicyFlaggedEvent"/> per flagged policy after a successful commit. The event carries the policy ID, the acting user ID (from <see cref="ICurrentUserService.UserId"/>), and the operation timestamp.</description></item>
///   <item><description>Invalidate the summary cache entry (<c>policy:v1:summary</c>) and the per-policy cache entry (<c>policy:v1:{id}</c>) for each flagged policy.</description></item>
/// </list>
/// <para>
/// Cache invalidation happens after a successful commit. If event publishing or cache
/// invalidation fails, the database state is already correct — those are best-effort
/// operations that do not cause the command to fail.
/// </para>
/// </remarks>
public sealed class FlagPoliciesCommandHandler(
    IPolicyRepository repository,
    IEventPublisher eventPublisher,
    ICacheService cache,
    ICurrentUserService currentUser,
    ILogger<FlagPoliciesCommandHandler> logger)
    : IRequestHandler<FlagPoliciesCommand>
{
    private const string SummaryCacheKey = "policy:v1:summary";

    private static string PolicyCacheKey(Guid id) => $"policy:v1:{id}";

    /// <inheritdoc/>
    public async Task Handle(FlagPoliciesCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling {Command} — flagging {Count} polic{Suffix}",
            nameof(FlagPoliciesCommand),
            command.PolicyIds.Count,
            command.PolicyIds.Count == 1 ? "y" : "ies");

        // ---- Step 1: Load all policies — fail fast on any missing ID ----
        var policies = new List<Domain.Entities.Policy>(command.PolicyIds.Count);

        foreach (var id in command.PolicyIds)
        {
            var policy = await repository.GetByIdAsync(id, cancellationToken);

            if (policy is null)
            {
                logger.LogWarning(
                    "{Command} — policy {PolicyId} not found",
                    nameof(FlagPoliciesCommand), id);

                throw new PolicyNotFoundException(id);
            }

            policies.Add(policy);
        }

        // ---- Step 2: Validate state — fail fast on any already-flagged policy ----
        foreach (var policy in policies)
        {
            if (policy.FlaggedForReview)
            {
                logger.LogWarning(
                    "{Command} — policy {PolicyId} is already flagged for review",
                    nameof(FlagPoliciesCommand), policy.Id);

                throw new InvalidPolicyStateException(
                    policy.Id,
                    "Policy is already flagged for review.");
            }
        }

        // ---- Step 3: Flag all and persist atomically ----
        var now = DateTimeOffset.UtcNow;

        foreach (var policy in policies)
            policy.Flag(now);

        await repository.UpdateRangeAsync(policies, cancellationToken);

        logger.LogInformation(
            "{Command} — persisted flag on {Count} polic{Suffix}",
            nameof(FlagPoliciesCommand),
            policies.Count,
            policies.Count == 1 ? "y" : "ies");

        // ---- Step 4: Publish one PolicyFlaggedEvent per policy ----
        var userId = currentUser.UserId ?? string.Empty;

        foreach (var policy in policies)
        {
            await eventPublisher.PublishAsync(
                new PolicyFlaggedEvent(policy.Id, userId, now),
                cancellationToken);
        }

        // ---- Step 5: Invalidate cache ----
        await cache.RemoveAsync(SummaryCacheKey, cancellationToken);

        foreach (var id in command.PolicyIds)
            await cache.RemoveAsync(PolicyCacheKey(id), cancellationToken);

        logger.LogInformation(
            "{Command} completed — invalidated summary cache and {Count} per-policy cache entr{Suffix}",
            nameof(FlagPoliciesCommand),
            command.PolicyIds.Count,
            command.PolicyIds.Count == 1 ? "y" : "ies");
    }
}
