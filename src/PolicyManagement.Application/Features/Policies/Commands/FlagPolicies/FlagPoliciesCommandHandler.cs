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
///   <item><description>Load all requested policies in a single batch query via <see cref="IPolicyRepository.GetByIdsAsync"/>. Throws <see cref="PolicyNotFoundException"/> for the first ID missing from the result set.</description></item>
///   <item><description>Validate state — throws <see cref="InvalidPolicyStateException"/> for the first policy that is already flagged.</description></item>
///   <item><description>Call <see cref="Domain.Entities.Policy.Flag"/> on each entity and persist all changes in a single call to <see cref="IPolicyRepository.UpdateRangeAsync"/>.</description></item>
///   <item><description>For each flagged policy: publish one <see cref="PolicyFlaggedEvent"/> and invalidate its per-policy cache entry (<c>policy:v1:{id}</c>). The event carries the policy ID, the acting user ID (from <see cref="ICurrentUserService.UserId"/>), and the operation timestamp.</description></item>
///   <item><description>Invalidate the summary cache entry (<c>policy:v1:summary</c>).</description></item>
/// </list>
/// <para>
/// <see cref="ICurrentUserService.UserId"/> is expected to be non-null when this command
/// is dispatched via the authenticated HTTP pipeline. The <c>[Authorize(Policy = "PolicyWrite")]</c>
/// attribute on the controller action ensures a valid JWT is present before MediatR is invoked.
/// An <see cref="InvalidOperationException"/> is thrown if <c>UserId</c> is <see langword="null"/>
/// at handler invocation time — this indicates a broken auth contract, not a normal domain error.
/// </para>
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

        // ---- Step 1: Load all policies in one batch query (single SQL IN clause) ----
        var policies = await repository.GetByIdsAsync(command.PolicyIds, cancellationToken);

        // Detect the first requested ID that was not returned by the repository.
        var missingId = command.PolicyIds.FirstOrDefault(id => policies.All(p => p.Id != id));
        if (missingId != default)
        {
            logger.LogWarning(
                "{Command} — policy {PolicyId} not found",
                nameof(FlagPoliciesCommand), missingId);

            throw new PolicyNotFoundException(missingId);
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

        // ---- Step 4: Publish one PolicyFlaggedEvent per policy and invalidate per-policy cache ----
        var userId = currentUser.UserId
            ?? throw new InvalidOperationException(
                "FlagPoliciesCommandHandler requires an authenticated user. " +
                "Ensure [Authorize(Policy = \"PolicyWrite\")] is applied to the controller action.");

        foreach (var policy in policies)
        {
            await eventPublisher.PublishAsync(
                new PolicyFlaggedEvent(policy.Id, userId, now),
                cancellationToken);

            await cache.RemoveAsync(PolicyCacheKey(policy.Id), cancellationToken);
        }

        // ---- Step 5: Invalidate summary cache ----
        await cache.RemoveAsync(SummaryCacheKey, cancellationToken);

        logger.LogInformation(
            "{Command} completed — invalidated summary cache and {Count} per-policy cache entr{Suffix}",
            nameof(FlagPoliciesCommand),
            command.PolicyIds.Count,
            command.PolicyIds.Count == 1 ? "y" : "ies");
    }
}
