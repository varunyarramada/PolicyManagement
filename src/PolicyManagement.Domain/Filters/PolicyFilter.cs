using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Domain.Filters;

/// <summary>
/// Encapsulates all filter, sort, and pagination parameters for querying the policy list.
/// Passed directly from the Application layer to <see cref="Interfaces.IPolicyRepository"/>.
/// This record contains no MediatR or Application layer dependencies.
/// </summary>
/// <param name="Page">1-based page number. Must be &gt;= 1.</param>
/// <param name="Size">Number of records per page. Must be between 1 and 100 inclusive.</param>
/// <param name="SortField">The field to sort by (e.g. "createdAt", "premiumAmount").</param>
/// <param name="SortDirection">Sort direction: "asc" or "desc".</param>
/// <param name="Status">Optional filter by <see cref="PolicyStatus"/>.</param>
/// <param name="LineOfBusiness">Optional filter by <see cref="Enums.LineOfBusiness"/>.</param>
/// <param name="Region">Optional filter by region string.</param>
/// <param name="EffectiveDateFrom">Optional inclusive start of the effective date range.</param>
/// <param name="EffectiveDateTo">Optional inclusive end of the effective date range.</param>
/// <param name="Search">
/// Optional case-insensitive substring match applied across
/// <c>PolicyNumber</c>, <c>PolicyholderName</c>, and <c>Underwriter</c>.
/// </param>
public sealed record PolicyFilter(
    int Page,
    int Size,
    string SortField,
    string SortDirection,
    PolicyStatus? Status,
    LineOfBusiness? LineOfBusiness,
    string? Region,
    DateOnly? EffectiveDateFrom,
    DateOnly? EffectiveDateTo,
    string? Search);
