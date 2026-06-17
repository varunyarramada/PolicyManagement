using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Filters;
using PolicyManagement.Domain.Models;

namespace PolicyManagement.Domain.Interfaces;

/// <summary>
/// Defines the data-access contract for <see cref="Policy"/> persistence.
/// Implementations live in <c>PolicyManagement.Infrastructure</c>.
/// Application layer handlers depend only on this interface — never on <c>DbContext</c> directly.
/// </summary>
public interface IPolicyRepository
{
    /// <summary>
    /// Retrieves a single policy by its unique identifier.
    /// Returns <see langword="null"/> if no matching, non-deleted policy is found.
    /// </summary>
    /// <param name="id">The unique identifier of the policy.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The matching <see cref="Policy"/>, or <see langword="null"/> if not found.</returns>
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged, filtered, and sorted list of policies together with the total
    /// count of records that match the filter (before paging).
    /// </summary>
    /// <param name="filter">Filter, sort, and pagination parameters.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A tuple containing the current page of results and the total matching record count.
    /// </returns>
    Task<(IReadOnlyList<Policy> Items, int TotalCount)> GetPagedAsync(
        PolicyFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregated summary statistics across all non-deleted policies.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="PolicySummaryData"/> record containing the aggregation results.</returns>
    Task<PolicySummaryData> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to a batch of existing policies within a single atomic operation.
    /// </summary>
    /// <param name="policies">The collection of policies to update.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    Task UpdateRangeAsync(IEnumerable<Policy> policies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether all of the supplied policy IDs correspond to existing,
    /// non-deleted policies.
    /// </summary>
    /// <param name="ids">The collection of policy IDs to verify.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="true"/> if every ID in <paramref name="ids"/> matches a non-deleted policy;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistAllAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
