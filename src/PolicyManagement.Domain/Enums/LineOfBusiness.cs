namespace PolicyManagement.Domain.Enums;

/// <summary>
/// Represents the line of business for an insurance policy.
/// </summary>
public enum LineOfBusiness
{
    /// <summary>Property insurance line of business.</summary>
    Property,

    /// <summary>Casualty insurance line of business.</summary>
    Casualty,

    /// <summary>Accident and Health insurance line of business.</summary>
    /// <remarks>
    /// When serialised to a string (e.g. stored in the database via EF Core or returned in
    /// API responses), this value <strong>must</strong> be represented as <c>"A&amp;H"</c>
    /// — not <c>"AH"</c> (the default <see cref="object.ToString"/> result).
    /// The Infrastructure <c>PolicyConfiguration</c> must apply a custom
    /// <c>ValueConverter</c> that maps this member to the string <c>"A&amp;H"</c> on write
    /// and parses <c>"A&amp;H"</c> back to <see cref="AH"/> on read.
    /// </remarks>
    AH,

    /// <summary>Marine insurance line of business.</summary>
    Marine
}
