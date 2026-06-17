namespace PolicyManagement.Application.DTOs;

/// <summary>
/// Pagination metadata included in every paged list response.
/// Maps to the <c>PaginationMeta</c> schema in the OpenAPI specification and is
/// embedded under the <c>pagination</c> property of <see cref="PagedPolicyResponse"/>.
/// </summary>
/// <param name="Page">Current 1-based page number.</param>
/// <param name="Size">Number of records requested per page (1–100).</param>
/// <param name="TotalCount">Total number of records matching the filter (before paging).</param>
/// <param name="TotalPages">
/// Total number of pages: <c>Ceiling(TotalCount / Size)</c>.
/// Returns <c>1</c> when <c>TotalCount</c> is zero to avoid a zero-page response.
/// </param>
public sealed record PaginationMeta(int Page, int Size, int TotalCount, int TotalPages)
{
    /// <summary>
    /// Creates a <see cref="PaginationMeta"/> instance with <c>TotalPages</c> computed
    /// automatically from <paramref name="totalCount"/> and <paramref name="size"/>.
    /// </summary>
    /// <param name="page">Current 1-based page number.</param>
    /// <param name="size">Page size.</param>
    /// <param name="totalCount">Total matching records.</param>
    /// <returns>A fully populated <see cref="PaginationMeta"/> instance.</returns>
    public static PaginationMeta Create(int page, int size, int totalCount)
    {
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / size);
        return new PaginationMeta(page, size, totalCount, totalPages);
    }
}
