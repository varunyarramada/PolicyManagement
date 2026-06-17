using FluentValidation;

namespace PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;

/// <summary>
/// Validates all parameters on <see cref="FlagPoliciesCommand"/> before the handler executes.
/// Failures are surfaced as <c>400 Bad Request</c> with field-level errors via
/// <c>ValidationPipelineBehavior</c> → <c>GlobalExceptionMiddleware</c>.
/// </summary>
public sealed class FlagPoliciesCommandValidator : AbstractValidator<FlagPoliciesCommand>
{
    /// <summary>Maximum number of policy IDs allowed in a single flag request.</summary>
    public const int MaxPolicyIds = 100;

    /// <summary>Initialises all validation rules.</summary>
    public FlagPoliciesCommandValidator()
    {
        RuleFor(c => c.PolicyIds)
            .NotEmpty()
            .WithMessage("'policyIds' must contain at least one policy ID.");

        RuleFor(c => c.PolicyIds)
            .Must(ids => ids.Count <= MaxPolicyIds)
            .When(c => c.PolicyIds is { Count: > 0 })
            .WithMessage($"'policyIds' must not contain more than {MaxPolicyIds} IDs per request.");

        RuleFor(c => c.PolicyIds)
            .Must(ids => ids.All(id => id != Guid.Empty))
            .When(c => c.PolicyIds is { Count: > 0 })
            .WithMessage("'policyIds' must not contain empty (all-zeros) GUIDs.");

        RuleFor(c => c.PolicyIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(c => c.PolicyIds is { Count: > 0 })
            .WithMessage("'policyIds' must not contain duplicate IDs.");
    }
}
