using MediatR;
using Microsoft.Extensions.Logging;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Mappings;
using PolicyManagement.Domain.Constants;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Filters;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicies;

/// <summary>
/// Handles <see cref="GetPoliciesQuery"/> and returns a <see cref="PagedPolicyResponse"/>.
/// <para>
/// The list endpoint is <strong>never cached</strong>. Each request is served directly from
/// the repository so that filters, sort, and pagination always reflect live data.
/// </para>
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item><description>Parse the <c>sort</c> expression into <see cref="PolicyFilter.SortField"/> and <see cref="SortDirection"/>.</description></item>
///   <item><description>Convert optional string parameters (<c>status</c>, <c>lineOfBusiness</c>) to their domain enum equivalents.</description></item>
///   <item><description>Build a <see cref="PolicyFilter"/> and delegate to <see cref="IPolicyRepository.GetPagedAsync"/>.</description></item>
///   <item><description>Map result entities to <see cref="PolicyDto"/> and wrap in <see cref="PagedPolicyResponse"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// Line-of-business string → enum mapping is sourced from
/// <see cref="GetPoliciesQueryValidator.LobParseMap"/> to keep a single source of truth.
/// </para>
/// </summary>
public sealed class GetPoliciesQueryHandler(
    IPolicyRepository repository,
    ILogger<GetPoliciesQueryHandler> logger)
    : IRequestHandler<GetPoliciesQuery, PagedPolicyResponse>
{
    /// <inheritdoc/>
    public async Task<PagedPolicyResponse> Handle(
        GetPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling {Query} — page {Page}, size {Size}, sort '{Sort}'",
            nameof(GetPoliciesQuery), query.Page, query.Size, query.Sort);

        var (sortField, sortDirection) = ParseSort(query.Sort);

        var status         = ParseStatus(query.Status);
        var lineOfBusiness = ParseLineOfBusiness(query.LineOfBusiness);

        var filter = new PolicyFilter(
            Page:               query.Page,
            Size:               query.Size,
            SortField:          sortField,
            SortDirection:      sortDirection,
            Status:             status,
            LineOfBusiness:     lineOfBusiness,
            Region:             query.Region,
            EffectiveDateFrom:  query.EffectiveDateFrom,
            EffectiveDateTo:    query.EffectiveDateTo,
            Search:             query.Search);

        var (items, totalCount) = await repository.GetPagedAsync(filter, cancellationToken);

        // Compute pagination once and reuse — avoids double allocation and keeps
        // the log and return value guaranteed to be consistent.
        var pagination = PaginationMeta.Create(query.Page, query.Size, totalCount);

        var dtos = items.Select(p => p.ToDto()).ToList().AsReadOnly();

        // Log after DTO mapping succeeds so the entry is only written when the
        // response is fully constructed. Entry-level context (page/size/sort) is
        // logged above; LoggingPipelineBehavior logs overall handler duration.
        logger.LogInformation(
            "{Query} returned {Count}/{Total} policies (page {Page}/{TotalPages})",
            nameof(GetPoliciesQuery),
            dtos.Count,
            totalCount,
            query.Page,
            pagination.TotalPages);

        return new PagedPolicyResponse(Data: dtos, Pagination: pagination);
    }

    /// <summary>
    /// Parses the sort expression (<c>"fieldName"</c> or <c>"fieldName,direction"</c>)
    /// into a sort-field string and <see cref="SortDirection"/>.
    /// Falls back to <c>createdAt / Desc</c> if the expression is empty or malformed.
    /// </summary>
    private static (string SortField, SortDirection Direction) ParseSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return (PolicySortFields.CreatedAt, SortDirection.Desc);

        var parts = sort.Split(',', StringSplitOptions.TrimEntries);
        var field = parts[0];

        if (!PolicySortFields.IsValid(field))
            field = PolicySortFields.CreatedAt;

        var direction = SortDirection.Desc;
        if (parts.Length >= 2 &&
            string.Equals(parts[1], "asc", StringComparison.OrdinalIgnoreCase))
        {
            direction = SortDirection.Asc;
        }

        return (field, direction);
    }

    /// <summary>
    /// Parses an optional status string to its <see cref="PolicyStatus"/> equivalent.
    /// Returns <see langword="null"/> when the input is absent.
    /// Validation has already confirmed the value is parseable if present.
    /// </summary>
    private static PolicyStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        return Enum.Parse<PolicyStatus>(status, ignoreCase: true);
    }

    /// <summary>
    /// Parses an optional line-of-business string (including <c>"A&amp;H"</c>) to its
    /// <see cref="LineOfBusiness"/> equivalent using the shared
    /// <see cref="GetPoliciesQueryValidator.LobParseMap"/>.
    /// Returns <see langword="null"/> when the input is absent.
    /// Validation has already confirmed the value is in the map if present.
    /// </summary>
    private static LineOfBusiness? ParseLineOfBusiness(string? lob)
    {
        if (string.IsNullOrWhiteSpace(lob))
            return null;

        return GetPoliciesQueryValidator.LobParseMap.TryGetValue(lob, out var value) ? value : null;
    }
}
