namespace PolicyManagement.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of an insurance policy.
/// </summary>
public enum PolicyStatus
{
    /// <summary>Policy is currently active and in force.</summary>
    Active,

    /// <summary>Policy has passed its expiry date.</summary>
    Expired,

    /// <summary>Policy is awaiting activation or approval.</summary>
    Pending,

    /// <summary>Policy has been cancelled.</summary>
    Cancelled
}
