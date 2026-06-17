namespace PolicyManagement.Domain.Interfaces;

/// <summary>
/// Marker interface for entities that carry audit timestamps.
/// The Infrastructure layer's <c>PolicyDbContext</c> uses this interface in
/// <c>ChangeTracker</c> interception to automatically update <see cref="UpdatedAt"/>
/// on every write without requiring callers to set it manually.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>Gets the UTC-aware timestamp at which the entity was first created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the UTC-aware timestamp at which the entity was last written.</summary>
    DateTimeOffset UpdatedAt { get; }
}
