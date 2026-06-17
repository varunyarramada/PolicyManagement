namespace PolicyManagement.Domain.Enums;

/// <summary>
/// Represents the direction of a sort operation on a policy query.
/// </summary>
public enum SortDirection
{
    /// <summary>Sort results in ascending order (lowest to highest, earliest to latest).</summary>
    Asc,

    /// <summary>Sort results in descending order (highest to lowest, latest to earliest).</summary>
    Desc
}
