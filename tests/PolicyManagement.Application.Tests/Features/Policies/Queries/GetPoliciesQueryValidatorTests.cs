using FluentAssertions;
using FluentValidation;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicies;
using Xunit;

namespace PolicyManagement.Application.Tests.Features.Policies.Queries;

/// <summary>
/// Unit tests for <see cref="GetPoliciesQueryValidator"/>.
/// Tests cover all required validation rules from the OpenAPI spec and
/// the reviewer's explicit scenarios.
/// </summary>
public sealed class GetPoliciesQueryValidatorTests
{
    private readonly GetPoliciesQueryValidator _validator = new();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<IEnumerable<string>> GetErrorsAsync(GetPoliciesQuery query)
    {
        var result = await _validator.ValidateAsync(query);
        return result.Errors.Select(e => e.ErrorMessage);
    }

    private async Task ShouldPassAsync(GetPoliciesQuery query)
    {
        var result = await _validator.ValidateAsync(query);
        result.IsValid.Should().BeTrue(
            because: $"query {query} should be valid but had errors: " +
                     string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    private async Task ShouldFailAsync(GetPoliciesQuery query, string becauseContains)
    {
        var result = await _validator.ValidateAsync(query);
        result.IsValid.Should().BeFalse(
            because: $"query {query} should fail validation");
        result.Errors.Should().Contain(
            e => e.ErrorMessage.Contains(becauseContains, StringComparison.OrdinalIgnoreCase),
            because: $"expected an error containing '{becauseContains}'");
    }

    // -----------------------------------------------------------------------
    // Happy path — defaults and valid values
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Validate_WhenAllDefaultValues_ShouldPass()
    {
        await ShouldPassAsync(new GetPoliciesQuery());
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Expired")]
    [InlineData("Pending")]
    [InlineData("Cancelled")]
    [InlineData("active")]   // case-insensitive
    public async Task Validate_WhenStatusIsValid_ShouldPass(string status)
    {
        await ShouldPassAsync(new GetPoliciesQuery(Status: status));
    }

    [Theory]
    [InlineData("Property")]
    [InlineData("Casualty")]
    [InlineData("A&H")]
    [InlineData("Marine")]
    [InlineData("property")] // case-insensitive
    public async Task Validate_WhenLineOfBusinessIsValid_ShouldPass(string lob)
    {
        await ShouldPassAsync(new GetPoliciesQuery(LineOfBusiness: lob));
    }

    [Theory]
    [InlineData("Singapore")]
    [InlineData("Hong Kong")]
    [InlineData("Australia")]
    [InlineData("Japan")]
    [InlineData("Thailand")]
    [InlineData("Indonesia")]
    [InlineData("Malaysia")]
    [InlineData("Philippines")]
    [InlineData("singapore")] // case-insensitive
    public async Task Validate_WhenRegionIsValid_ShouldPass(string region)
    {
        await ShouldPassAsync(new GetPoliciesQuery(Region: region));
    }

    [Theory]
    [InlineData("createdAt,desc")]
    [InlineData("premiumAmount,asc")]
    [InlineData("policyNumber,DESC")] // case-insensitive direction
    [InlineData("status")]            // direction omitted — valid
    [InlineData("expiryDate,asc")]
    public async Task Validate_WhenSortIsValid_ShouldPass(string sort)
    {
        await ShouldPassAsync(new GetPoliciesQuery(Sort: sort));
    }

    [Fact]
    public async Task Validate_WhenSizeIsMaxAllowed_ShouldPass()
    {
        await ShouldPassAsync(new GetPoliciesQuery(Size: 100));
    }

    [Fact]
    public async Task Validate_WhenSizeIsMinAllowed_ShouldPass()
    {
        await ShouldPassAsync(new GetPoliciesQuery(Size: 1));
    }

    // -----------------------------------------------------------------------
    // CRIT: size above 100 fails validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Validate_WhenSizeAbove100_ShouldFail()
    {
        await ShouldFailAsync(new GetPoliciesQuery(Size: 101), "size");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_WhenSizeIsZeroOrNegative_ShouldFail(int size)
    {
        await ShouldFailAsync(new GetPoliciesQuery(Size: size), "size");
    }

    // -----------------------------------------------------------------------
    // CRIT: page < 1 fails validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Validate_WhenPageIsLessThan1_ShouldFail(int page)
    {
        await ShouldFailAsync(new GetPoliciesQuery(Page: page), "page");
    }

    // -----------------------------------------------------------------------
    // CRIT: invalid status fails validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Unknown")]
    [InlineData("INVALID")]
    [InlineData("Draft")]
    public async Task Validate_WhenStatusIsInvalid_ShouldFail(string status)
    {
        await ShouldFailAsync(new GetPoliciesQuery(Status: status), "status");
    }

    // -----------------------------------------------------------------------
    // CRIT: invalid sortField fails validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("unknownField")]
    [InlineData("createdAt,invalid")]  // direction is invalid
    [InlineData("createdAt,desc,extra")] // too many parts
    [InlineData("")]                   // empty string — caught by IsNullOrWhiteSpace guard
    [InlineData("  ")]                 // whitespace only — caught by IsNullOrWhiteSpace guard
    public async Task Validate_WhenSortIsInvalid_ShouldFail(string sort)
    {
        await ShouldFailAsync(new GetPoliciesQuery(Sort: sort), "sort");
    }

    // -----------------------------------------------------------------------
    // CRIT: invalid region fails validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("USA")]
    [InlineData("Europe")]
    [InlineData("New Zealand")]
    public async Task Validate_WhenRegionIsInvalid_ShouldFail(string region)
    {
        await ShouldFailAsync(new GetPoliciesQuery(Region: region), "region");
    }

    // -----------------------------------------------------------------------
    // CRIT: invalid lineOfBusiness fails validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Unknown")]
    [InlineData("AH")]       // must be "A&H" not "AH"
    [InlineData("Accident")]
    public async Task Validate_WhenLineOfBusinessIsInvalid_ShouldFail(string lob)
    {
        await ShouldFailAsync(new GetPoliciesQuery(LineOfBusiness: lob), "lineOfBusiness");
    }

    // -----------------------------------------------------------------------
    // Date range consistency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Validate_WhenEffectiveDateToIsBeforeFrom_ShouldFail()
    {
        var query = new GetPoliciesQuery(
            EffectiveDateFrom: new DateOnly(2024, 6, 1),
            EffectiveDateTo:   new DateOnly(2024, 1, 1));

        await ShouldFailAsync(query, "effectiveDateTo");
    }

    [Fact]
    public async Task Validate_WhenEffectiveDateToEqualsFrom_ShouldPass()
    {
        var date = new DateOnly(2024, 6, 1);
        await ShouldPassAsync(new GetPoliciesQuery(
            EffectiveDateFrom: date,
            EffectiveDateTo:   date));
    }

    [Fact]
    public async Task Validate_WhenOnlyEffectiveDateFromProvided_ShouldPass()
    {
        await ShouldPassAsync(new GetPoliciesQuery(EffectiveDateFrom: new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public async Task Validate_WhenOnlyEffectiveDateToProvided_ShouldPass()
    {
        await ShouldPassAsync(new GetPoliciesQuery(EffectiveDateTo: new DateOnly(2024, 12, 31)));
    }
}
