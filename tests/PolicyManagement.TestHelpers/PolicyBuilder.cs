using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.TestHelpers;

/// <summary>
/// Fluent builder for constructing <see cref="Policy"/> test instances using the
/// domain's <see cref="Policy.Create"/> factory method.
/// Shared across all test projects (Domain, Application, Infrastructure, API).
/// All defaults produce a valid, non-deleted Active policy.
/// </summary>
public sealed class PolicyBuilder
{
    private Guid _id                 = Guid.NewGuid();
    private string _policyNumber     = "POL-000001";
    private string _policyholderName = "Test Holder";
    private LineOfBusiness _lob      = LineOfBusiness.Property;
    private PolicyStatus _status     = PolicyStatus.Active;
    private decimal _premium         = 10_000m;
    private string _currency         = "USD";
    private DateOnly _effectiveDate  = new(2024, 1, 1);
    private DateOnly _expiryDate     = new(2025, 1, 1);
    private string _region           = "Singapore";
    private string _underwriter      = "Test Underwriter";
    private DateTimeOffset _now      = DateTimeOffset.UtcNow;

    /// <summary>Sets the policy ID.</summary>
    public PolicyBuilder WithId(Guid id)                      { _id = id;                   return this; }
    /// <summary>Sets the policy number.</summary>
    public PolicyBuilder WithPolicyNumber(string n)           { _policyNumber = n;          return this; }
    /// <summary>Sets the policyholder name.</summary>
    public PolicyBuilder WithPolicyholderName(string n)       { _policyholderName = n;      return this; }
    /// <summary>Sets the line of business.</summary>
    public PolicyBuilder WithLineOfBusiness(LineOfBusiness l) { _lob = l;                   return this; }
    /// <summary>Sets the policy status.</summary>
    public PolicyBuilder WithStatus(PolicyStatus s)           { _status = s;                return this; }
    /// <summary>Sets the premium amount.</summary>
    public PolicyBuilder WithPremium(decimal p)               { _premium = p;               return this; }
    /// <summary>Sets the currency code.</summary>
    public PolicyBuilder WithCurrency(string c)               { _currency = c;              return this; }
    /// <summary>Sets the effective date.</summary>
    public PolicyBuilder WithEffectiveDate(DateOnly d)        { _effectiveDate = d;         return this; }
    /// <summary>Sets the expiry date.</summary>
    public PolicyBuilder WithExpiryDate(DateOnly d)           { _expiryDate = d;            return this; }
    /// <summary>Sets the region.</summary>
    public PolicyBuilder WithRegion(string r)                 { _region = r;                return this; }
    /// <summary>Sets the underwriter name.</summary>
    public PolicyBuilder WithUnderwriter(string u)            { _underwriter = u;           return this; }
    /// <summary>Sets the audit timestamp used for CreatedAt / UpdatedAt.</summary>
    public PolicyBuilder WithNow(DateTimeOffset now)          { _now = now;                 return this; }

    /// <summary>Builds a <see cref="Policy"/> from the configured values.</summary>
    public Policy Build() =>
        Policy.Create(
            id:               _id,
            policyNumber:     _policyNumber,
            policyholderName: _policyholderName,
            lineOfBusiness:   _lob,
            status:           _status,
            premiumAmount:    _premium,
            currency:         _currency,
            effectiveDate:    _effectiveDate,
            expiryDate:       _expiryDate,
            region:           _region,
            underwriter:      _underwriter,
            now:              _now);

    /// <summary>
    /// Creates a list of <paramref name="count"/> distinct valid policies
    /// with sequential policy numbers and unique GUIDs.
    /// </summary>
    public static IReadOnlyList<Policy> BuildMany(int count)
    {
        var results = new List<Policy>(count);
        for (var i = 1; i <= count; i++)
        {
            results.Add(new PolicyBuilder()
                .WithId(Guid.NewGuid())
                .WithPolicyNumber($"POL-{i:D6}")
                .Build());
        }
        return results.AsReadOnly();
    }
}
