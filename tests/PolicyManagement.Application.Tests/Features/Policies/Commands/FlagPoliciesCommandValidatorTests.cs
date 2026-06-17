using FluentAssertions;
using FluentValidation.TestHelper;
using PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;
using Xunit;

namespace PolicyManagement.Application.Tests.Features.Policies.Commands;

/// <summary>
/// Unit tests for <see cref="FlagPoliciesCommandValidator"/>.
/// </summary>
public sealed class FlagPoliciesCommandValidatorTests
{
    private readonly FlagPoliciesCommandValidator _validator = new();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<Guid> ValidIds(int count = 3) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList().AsReadOnly();

    // -----------------------------------------------------------------------
    // Empty list
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_WhenPolicyIdsIsEmpty_ShouldFailValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(new List<Guid>().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.PolicyIds);
    }

    [Fact]
    public void Validate_WhenPolicyIdsIsEmpty_ShouldHaveDescriptiveErrorMessage()
    {
        // Arrange
        var command = new FlagPoliciesCommand(new List<Guid>().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("at least one", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Maximum count
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_WhenPolicyIdsHasExactly100Ids_ShouldPassValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(ValidIds(100));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenPolicyIdsHasMoreThan100Ids_ShouldFailValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(ValidIds(101));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.PolicyIds);
    }

    [Fact]
    public void Validate_WhenPolicyIdsHasMoreThan100Ids_ShouldHaveDescriptiveErrorMessage()
    {
        // Arrange
        var command = new FlagPoliciesCommand(ValidIds(101));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Duplicate GUIDs
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_WhenPolicyIdsHasDuplicates_ShouldFailValidation()
    {
        // Arrange
        var id = Guid.NewGuid();
        var command = new FlagPoliciesCommand(new[] { id, Guid.NewGuid(), id }.ToList().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.PolicyIds);
    }

    [Fact]
    public void Validate_WhenPolicyIdsHasDuplicates_ShouldHaveDescriptiveErrorMessage()
    {
        // Arrange
        var id = Guid.NewGuid();
        var command = new FlagPoliciesCommand(new[] { id, id }.ToList().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Empty GUIDs (Guid.Empty)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_WhenPolicyIdsContainsEmptyGuid_ShouldFailValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(
            new[] { Guid.NewGuid(), Guid.Empty, Guid.NewGuid() }.ToList().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.PolicyIds);
    }

    [Fact]
    public void Validate_WhenPolicyIdsContainsEmptyGuid_ShouldHaveDescriptiveErrorMessage()
    {
        // Arrange
        var command = new FlagPoliciesCommand(new[] { Guid.Empty }.ToList().AsReadOnly());

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_WhenSingleValidId_ShouldPassValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(ValidIds(1));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenMultipleUniqueValidIds_ShouldPassValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(ValidIds(50));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
