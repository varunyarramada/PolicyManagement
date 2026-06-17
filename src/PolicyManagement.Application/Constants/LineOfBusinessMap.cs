using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Application.Constants;

/// <summary>
/// Maps API-facing line-of-business display strings (including <c>"A&amp;H"</c>)
/// to their <see cref="LineOfBusiness"/> domain enum equivalents.
/// </summary>
/// <remarks>
/// This is the single source of truth for LOB string → enum parsing across the
/// Application layer. Both <c>GetPoliciesQueryValidator</c> and
/// <c>GetPoliciesQueryHandler</c> (and any future handlers that need LOB parsing)
/// reference this map rather than each other.
/// </remarks>
internal static class LineOfBusinessMap
{
    /// <summary>
    /// Case-insensitive dictionary mapping display names to <see cref="LineOfBusiness"/> values.
    /// <c>"A&amp;H"</c> maps to <see cref="LineOfBusiness.AH"/>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, LineOfBusiness> DisplayToEnum =
        new Dictionary<string, LineOfBusiness>(StringComparer.OrdinalIgnoreCase)
        {
            ["Property"] = LineOfBusiness.Property,
            ["Casualty"]  = LineOfBusiness.Casualty,
            ["A&H"]       = LineOfBusiness.AH,
            ["Marine"]    = LineOfBusiness.Marine,
        };
}
