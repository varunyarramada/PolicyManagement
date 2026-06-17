using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Filters;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.Domain.Models;
using PolicyManagement.Infrastructure.Persistence;

namespace PolicyManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPolicyRepository"/>.
/// All read queries use <c>.AsNoTracking()</c>.
/// Filtering, sorting, and pagination are composed here — never in handlers.
/// </summary>
public sealed class PolicyRepository(PolicyDbContext dbContext) : IPolicyRepository
{
    /// <inheritdoc/>
    public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.Policies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Policy> Items, int TotalCount)> GetPagedAsync(
        PolicyFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Policies.AsNoTracking();

        // ---- Filters ----
        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        if (filter.LineOfBusiness.HasValue)
            query = query.Where(p => p.LineOfBusiness == filter.LineOfBusiness.Value);

        if (!string.IsNullOrWhiteSpace(filter.Region))
            query = query.Where(p => p.Region == filter.Region);

        if (filter.EffectiveDateFrom.HasValue)
            query = query.Where(p => p.EffectiveDate >= filter.EffectiveDateFrom.Value);

        if (filter.EffectiveDateTo.HasValue)
            query = query.Where(p => p.EffectiveDate <= filter.EffectiveDateTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // SQL Server's default Latin1_General_CI_AS collation is case-insensitive.
            // Calling .ToLower() is redundant, adds a computed LOWER() call that prevents
            // index usage on policy_number and policyholder_name, and is therefore omitted.
            query = query.Where(p =>
                p.PolicyNumber.Contains(filter.Search) ||
                p.PolicyholderName.Contains(filter.Search) ||
                p.Underwriter.Contains(filter.Search));
        }

        // ---- Total count (before paging) ----
        var totalCount = await query.CountAsync(cancellationToken);

        // ---- Sort ----
        query = ApplySort(query, filter.SortField, filter.SortDirection);

        // ---- Pagination ----
        var skip = (filter.Page - 1) * filter.Size;
        var items = await query
            .Skip(skip)
            .Take(filter.Size)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc/>
    public async Task<PolicySummaryData> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        // TODO: This implementation loads all non-deleted policy rows into the .NET process
        // before performing grouping/aggregation in LINQ-to-objects. For the 210-row seed
        // dataset this is acceptable. Once the Policies table grows beyond ~10,000 rows,
        // consider replacing with server-side SQL aggregation (multiple targeted GROUP BY
        // queries or a database view) to avoid loading all rows across the network.
        // The summary result is cached (policy:v1:summary, TTL 60s) to reduce frequency.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thirtyDaysFromNow = today.AddDays(30);

        var policies = await dbContext.Policies
            .AsNoTracking()
            .Select(p => new
            {
                p.Status,
                p.LineOfBusiness,
                p.Region,
                p.Currency,
                p.PremiumAmount,
                p.FlaggedForReview,
                p.ExpiryDate
            })
            .ToListAsync(cancellationToken);

        var totalPolicies = policies.Count;
        var flaggedCount = policies.Count(p => p.FlaggedForReview);
        var expiringSoonCount = policies.Count(p =>
            p.Status == PolicyStatus.Active &&
            p.ExpiryDate >= today &&
            p.ExpiryDate <= thirtyDaysFromNow);

        var totalPremium = policies.Sum(p => p.PremiumAmount);

        var countByStatus = policies
            .GroupBy(p => p.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var countByLob = policies
            .GroupBy(p => p.LineOfBusiness)
            .ToDictionary(g => g.Key, g => g.Count());

        var countByRegion = policies
            .GroupBy(p => p.Region)
            .ToDictionary(g => g.Key, g => g.Count());

        var premiumByCurrency = policies
            .GroupBy(p => p.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.PremiumAmount));

        return new PolicySummaryData(
            TotalPolicies: totalPolicies,
            TotalPremium: totalPremium,
            FlaggedCount: flaggedCount,
            ExpiringSoonCount: expiringSoonCount,
            CountByStatus: countByStatus,
            CountByLineOfBusiness: countByLob,
            CountByRegion: countByRegion,
            PremiumByCurrency: premiumByCurrency);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Policy>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await dbContext.Policies
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateRangeAsync(
        IEnumerable<Policy> policies,
        CancellationToken cancellationToken = default)
    {
        dbContext.Policies.UpdateRange(policies);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistAllAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var foundCount = await dbContext.Policies
            .AsNoTracking()
            .CountAsync(p => idList.Contains(p.Id), cancellationToken);
        return foundCount == idList.Count;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static IQueryable<Policy> ApplySort(
        IQueryable<Policy> query,
        string sortField,
        Domain.Enums.SortDirection direction)
    {
        var isDesc = direction == Domain.Enums.SortDirection.Desc;

        return sortField.ToLowerInvariant() switch
        {
            "policynumber"      => isDesc ? query.OrderByDescending(p => p.PolicyNumber)      : query.OrderBy(p => p.PolicyNumber),
            "policyholdername"  => isDesc ? query.OrderByDescending(p => p.PolicyholderName)  : query.OrderBy(p => p.PolicyholderName),
            "status"            => isDesc ? query.OrderByDescending(p => p.Status)             : query.OrderBy(p => p.Status),
            "lineofbusiness"    => isDesc ? query.OrderByDescending(p => p.LineOfBusiness)     : query.OrderBy(p => p.LineOfBusiness),
            "region"            => isDesc ? query.OrderByDescending(p => p.Region)             : query.OrderBy(p => p.Region),
            "premiumamount"     => isDesc ? query.OrderByDescending(p => p.PremiumAmount)      : query.OrderBy(p => p.PremiumAmount),
            "currency"          => isDesc ? query.OrderByDescending(p => p.Currency)           : query.OrderBy(p => p.Currency),
            "effectivedate"     => isDesc ? query.OrderByDescending(p => p.EffectiveDate)      : query.OrderBy(p => p.EffectiveDate),
            "expirydate"        => isDesc ? query.OrderByDescending(p => p.ExpiryDate)         : query.OrderBy(p => p.ExpiryDate),
            "underwriter"       => isDesc ? query.OrderByDescending(p => p.Underwriter)        : query.OrderBy(p => p.Underwriter),
            "updatedat"         => isDesc ? query.OrderByDescending(p => p.UpdatedAt)          : query.OrderBy(p => p.UpdatedAt),
            _                   => isDesc ? query.OrderByDescending(p => p.CreatedAt)          : query.OrderBy(p => p.CreatedAt), // default: createdAt
        };
    }
}
