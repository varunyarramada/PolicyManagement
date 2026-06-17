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
/// </summary>
public sealed class GetPoliciesQueryHandler(
    IPolicyRepository repository,
    ILogger<GetPoliciesQueryHandler> logger)
    : IRequestHandler<GetPoliciesQuery, PagedPolicyResponse>
{
    /// <summary>
    /// Maps the API-facing line-of-business display strings (including <c>"A&amp;H"</c>)
    /// to their domain enum equivalents.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, LineOfBusiness> LobParseMap =
        new Dictionary<string, LineOfBusiness>(StringComparer.OrdinalIgnoreCase)
        {
            ["Property"] = LineOfBusiness.Property,
            ["Casualty"]  = LineOfBusiness.Casualty,
            ["A&H"]       = LineOfBusiness.AH,
            ["Marine"]    = LineOfBusiness.Marine,
        };

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

        logger.LogInformation(
            "{Query} returned {Count}/{Total} policies (page {Page}/{TotalPages})",
            nameof(GetPoliciesQuery),
            items.Count,
            totalCount,
            query.Page,
            PaginationMeta.Create(query.Page, query.Size, totalCount).TotalPages);

        var dtos = items.Select(p => p.ToDto()).ToList().AsReadOnly();

        return new PagedPolicyResponse(
            Data:       dtos,
            Pagination: PaginationMeta.Create(query.Page, query.Size, totalCount));
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
    /// <see cref="LineOfBusiness"/> equivalent.
    /// Returns <see langword="null"/> when the input is absent.
    /// Validation has already confirmed the value is in <see cref="LobParseMap"/> if present.
    /// </summary>
    private static LineOfBusiness? ParseLineOfBusiness(string? lob)
    {
        if (string.IsNullOrWhiteSpace(lob))
            return null;

        return LobParseMap.TryGetValue(lob, out var value) ? value : null;
    }
}
