using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Domain.Entities;

/// <summary>
/// Represents an insurance policy in the Chubb APAC policy management system.
/// This is the aggregate root for the Policy domain.
/// All properties use private/init setters to enforce encapsulation.
/// No EF Core annotations are applied — all mapping is handled by
/// <c>PolicyManagement.Infrastructure</c> via Fluent API.
/// </summary>
public class Policy : IAuditableEntity
{
    /// <summary>Gets the unique identifier for the policy (client-generated GUID).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the unique policy number in the format <c>POL-XXXXXX</c>.
    /// </summary>
    public string PolicyNumber { get; private set; } = string.Empty;

    /// <summary>Gets the full name of the policyholder.</summary>
    public string PolicyholderName { get; private set; } = string.Empty;

    /// <summary>Gets the line of business classification for this policy.</summary>
    public LineOfBusiness LineOfBusiness { get; private set; }

    /// <summary>Gets the current lifecycle status of this policy.</summary>
    public PolicyStatus Status { get; private set; }

    /// <summary>
    /// Gets the premium amount for this policy.
    /// Valid range: 1,000.00 – 5,000,000.00.
    /// </summary>
    public decimal PremiumAmount { get; private set; }

    /// <summary>
    /// Gets the ISO 4217 currency code for the premium amount.
    /// Supported values: USD, SGD, HKD, AUD, JPY, THB.
    /// </summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Gets the date on which this policy becomes effective.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>
    /// Gets the date on which this policy expires.
    /// Must be after <see cref="EffectiveDate"/>.
    /// </summary>
    public DateOnly ExpiryDate { get; private set; }

    /// <summary>Gets the APAC region in which this policy is issued.</summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>Gets the name of the underwriter responsible for this policy.</summary>
    public string Underwriter { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this policy has been flagged for review.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool FlaggedForReview { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this policy has been soft-deleted.
    /// Soft-deleted policies are excluded from all queries via a global EF Core query filter.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Gets the UTC-aware timestamp at which this policy record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC-aware timestamp at which this policy record was last updated.
    /// Set on insert and updated on every write.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Parameterless constructor required by EF Core for materialisation.
    /// Do not call directly from application code — use <see cref="Create"/> instead.
    /// </summary>
    private Policy() { }

    /// <summary>
    /// Creates a new <see cref="Policy"/> with the supplied values.
    /// </summary>
    /// <param name="id">Client-generated GUID unique identifier.</param>
    /// <param name="policyNumber">Unique policy number in format <c>POL-XXXXXX</c>.</param>
    /// <param name="policyholderName">Full name of the policyholder.</param>
    /// <param name="lineOfBusiness">Line of business classification.</param>
    /// <param name="status">Initial lifecycle status.</param>
    /// <param name="premiumAmount">Premium amount (1,000.00 – 5,000,000.00).</param>
    /// <param name="currency">ISO 4217 currency code.</param>
    /// <param name="effectiveDate">Date the policy becomes effective.</param>
    /// <param name="expiryDate">Date the policy expires. Must be after <paramref name="effectiveDate"/>.</param>
    /// <param name="region">APAC region string.</param>
    /// <param name="underwriter">Name of the underwriter.</param>
    /// <param name="now">Current UTC timestamp used for audit fields.</param>
    /// <returns>A fully initialised <see cref="Policy"/> instance.</returns>
    public static Policy Create(
        Guid id,
        string policyNumber,
        string policyholderName,
        LineOfBusiness lineOfBusiness,
        PolicyStatus status,
        decimal premiumAmount,
        string currency,
        DateOnly effectiveDate,
        DateOnly expiryDate,
        string region,
        string underwriter,
        DateTimeOffset now)
    {
        if (expiryDate <= effectiveDate)
            throw new InvalidPolicyStateException(id, "Expiry date must be after effective date.");

        if (premiumAmount <= 0)
            throw new InvalidPolicyStateException(id, "Premium amount must be positive.");

        return new Policy
        {
            Id = id,
            PolicyNumber = policyNumber,
            PolicyholderName = policyholderName,
            LineOfBusiness = lineOfBusiness,
            Status = status,
            PremiumAmount = premiumAmount,
            Currency = currency,
            EffectiveDate = effectiveDate,
            ExpiryDate = expiryDate,
            Region = region,
            Underwriter = underwriter,
            FlaggedForReview = false,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Marks this policy as flagged for review and updates the <see cref="UpdatedAt"/> timestamp.
    /// </summary>
    /// <param name="now">Current UTC timestamp.</param>
    public void Flag(DateTimeOffset now)
    {
        FlaggedForReview = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Soft-deletes this policy. Once deleted, the policy is excluded from all repository queries
    /// via the global EF Core query filter.
    /// </summary>
    /// <param name="now">Current UTC timestamp.</param>
    public void SoftDelete(DateTimeOffset now)
    {
        IsDeleted = true;
        UpdatedAt = now;
    }
}
