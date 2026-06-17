using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Application.Tests.Helpers;

/// <summary>
/// Fluent builder for constructing <see cref="Policy"/> test instances using the
/// domain's <see cref="Policy.Create"/> factory method.
/// All defaults produce a valid, non-deleted Active policy.
/// </summary>
internal sealed class PolicyBuilder
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

    internal PolicyBuilder WithId(Guid id)                      { _id = id;                   return this; }
    internal PolicyBuilder WithPolicyNumber(string n)           { _policyNumber = n;          return this; }
    internal PolicyBuilder WithPolicyholderName(string n)       { _policyholderName = n;      return this; }
    internal PolicyBuilder WithLineOfBusiness(LineOfBusiness l) { _lob = l;                   return this; }
    internal PolicyBuilder WithStatus(PolicyStatus s)           { _status = s;                return this; }
    internal PolicyBuilder WithPremium(decimal p)               { _premium = p;               return this; }
    internal PolicyBuilder WithCurrency(string c)               { _currency = c;              return this; }
    internal PolicyBuilder WithEffectiveDate(DateOnly d)        { _effectiveDate = d;         return this; }
    internal PolicyBuilder WithExpiryDate(DateOnly d)           { _expiryDate = d;            return this; }
    internal PolicyBuilder WithRegion(string r)                 { _region = r;                return this; }
    internal PolicyBuilder WithUnderwriter(string u)            { _underwriter = u;           return this; }
    internal PolicyBuilder WithNow(DateTimeOffset now)          { _now = now;                 return this; }

    internal Policy Build() =>
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

    /// <summary>Creates a list of <paramref name="count"/> distinct policies with sequential numbers.</summary>
    internal static IReadOnlyList<Policy> BuildMany(int count)
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
