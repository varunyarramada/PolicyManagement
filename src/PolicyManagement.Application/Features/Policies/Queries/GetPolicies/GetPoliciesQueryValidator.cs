using FluentValidation;
using PolicyManagement.Application.Constants;
using PolicyManagement.Domain.Constants;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Application.Features.Policies.Queries.GetPolicies;

/// <summary>
/// Validates all parameters on <see cref="GetPoliciesQuery"/> before the handler executes.
/// Failures are surfaced as <c>400 Bad Request</c> with field-level errors via
/// <c>ValidationPipelineBehavior</c> → <c>GlobalExceptionMiddleware</c>.
/// </summary>
public sealed class GetPoliciesQueryValidator : AbstractValidator<GetPoliciesQuery>
{
    /// <summary>Initialises all validation rules.</summary>
    public GetPoliciesQueryValidator()
    {
        // ---- Pagination ----
        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("'page' must be greater than or equal to 1.");

        RuleFor(q => q.Size)
            .InclusiveBetween(1, 100)
            .WithMessage("'size' must be between 1 and 100.");

        // ---- Sort ----
        RuleFor(q => q.Sort)
            .Must(s => !string.IsNullOrWhiteSpace(s))
            .WithMessage("'sort' must not be empty or whitespace. Default: 'createdAt,desc'.")
            .Must(BeAValidSortExpression)
            .When(q => !string.IsNullOrWhiteSpace(q.Sort), ApplyConditionTo.CurrentValidator)
            .WithMessage(
                q => $"'sort' value '{q.Sort}' is invalid. " +
                     $"Expected format: 'fieldName[,asc|desc]'. " +
                     $"Allowed fields: {string.Join(", ", PolicySortFields.All.Order())}.");

        // ---- Optional enum filters ----
        RuleFor(q => q.Status)
            .Must(s => s == null || Enum.TryParse<PolicyStatus>(s, ignoreCase: true, out _))
            .WithMessage(
                q => $"'status' value '{q.Status}' is not valid. " +
                     $"Allowed values: {string.Join(", ", Enum.GetNames<PolicyStatus>())}.");

        RuleFor(q => q.LineOfBusiness)
            .Must(lob => lob == null || LineOfBusinessMap.DisplayToEnum.ContainsKey(lob))
            .WithMessage(
                q => $"'lineOfBusiness' value '{q.LineOfBusiness}' is not valid. " +
                     $"Allowed values: {string.Join(", ", LineOfBusinessMap.DisplayToEnum.Keys.Order())}.");

        RuleFor(q => q.Region)
            .Must(r => r == null || Regions.IsValid(r))
            .WithMessage(
                q => $"'region' value '{q.Region}' is not valid. " +
                     $"Allowed values: {string.Join(", ", Regions.All.Order())}.");

        // ---- Date range consistency ----
        RuleFor(q => q.EffectiveDateTo)
            .GreaterThanOrEqualTo(q => q.EffectiveDateFrom)
            .When(q => q.EffectiveDateFrom.HasValue && q.EffectiveDateTo.HasValue)
            .WithMessage("'effectiveDateTo' must be on or after 'effectiveDateFrom'.");
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="sort"/> is a valid sort expression.
    /// A valid expression is <c>fieldName</c> or <c>fieldName,asc|desc</c> (case-insensitive).
    /// Only called when <paramref name="sort"/> is non-empty (guarded by the <c>When</c> condition).
    /// </summary>
    private static bool BeAValidSortExpression(string sort)
    {
        var parts = sort.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length > 2)
            return false;

        if (!PolicySortFields.IsValid(parts[0]))
            return false;

        if (parts.Length == 2)
        {
            var direction = parts[1];
            return string.Equals(direction, "asc",  StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
