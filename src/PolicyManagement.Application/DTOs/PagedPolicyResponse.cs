namespace PolicyManagement.Application.DTOs;

/// <summary>
/// Paginated list response for <c>GET /api/v1/policies</c>.
/// Maps directly to the <c>PagedPolicyResponse</c> schema in the OpenAPI specification:
/// a <c>data</c> array of <see cref="PolicyDto"/> items and a nested <c>pagination</c>
/// metadata object.
/// </summary>
/// <param name="Data">The policy records on the current page.</param>
/// <param name="Pagination">Pagination metadata (page, size, totalCount, totalPages).</param>
public sealed record PagedPolicyResponse(
    IReadOnlyList<PolicyDto> Data,
    PaginationMeta Pagination);
