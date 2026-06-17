namespace PolicyManagement.Application.DTOs;

/// <summary>
/// Generic wrapper for paginated list responses. Used by <c>GET /api/v1/policies</c>.
/// Carries both the current page of items and the pagination metadata required
/// by the frontend to render page controls.
/// </summary>
/// <typeparam name="T">The type of each item in the page. For policies, this is <see cref="PolicyDto"/>.</typeparam>
/// <param name="Items">The items on the current page.</param>
/// <param name="Page">Current 1-based page number.</param>
/// <param name="Size">Number of items requested per page.</param>
/// <param name="TotalCount">Total number of records matching the filter (before paging).</param>
public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int Size,
    int TotalCount)
{
    /// <summary>
    /// Gets the total number of pages. Calculated as <c>Ceiling(TotalCount / Size)</c>.
    /// Returns <c>1</c> when <c>TotalCount</c> is zero to avoid a zero-page response.
    /// </summary>
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / Size);
}
