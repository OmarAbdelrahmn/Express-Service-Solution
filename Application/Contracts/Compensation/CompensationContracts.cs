using Domain.Entities.AccountingPlatform;
using FluentValidation;

namespace Application.Contracts.Compensation;

public record CreateCompensationRuleRequest(
    string Code,
    string Name,
    CompensationRuleTemplate Template,
    CompensationComponentType ComponentType,
    string MetricCode,
    string? ConditionMetricCode,
    string? ConditionOperator,
    decimal? ConditionValue,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? Rate,
    decimal? BelowRate,
    decimal? AboveRate,
    decimal? FixedAmount,
    decimal? BaseAmount,
    string? TargetComponentCode,
    int Priority,
    string? ExclusiveGroup,
    CompensationStackingMode StackingMode,
    int RoundingScale = 2);

public record CreateCompensationPolicyRequest(
    int LegalEntityId,
    int PlatformAccountId,
    string WorkerCategory,
    string Code,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyCollection<CreateCompensationRuleRequest> Rules);

public record ActivateCompensationPolicyRequest(string? RowVersion);
public record CloneCompensationPolicyVersionRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public record RetireCompensationPolicyRequest(string? RowVersion, string? Comment);
public record SimulateCompensationPolicyRequest(IReadOnlyDictionary<string, decimal> Metrics);

public record CompensationRuleResponse(
    Guid Id,
    Guid CompensationPolicyVersionId,
    string Code,
    string Name,
    CompensationRuleTemplate Template,
    CompensationComponentType ComponentType,
    string MetricCode,
    string? ConditionMetricCode,
    string? ConditionOperator,
    decimal? ConditionValue,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? Rate,
    decimal? BelowRate,
    decimal? AboveRate,
    decimal? FixedAmount,
    decimal? BaseAmount,
    string? TargetComponentCode,
    int Priority,
    string? ExclusiveGroup,
    CompensationStackingMode StackingMode,
    int RoundingScale,
    bool IsActive);

public record CompensationPolicyResponse(
    Guid Id,
    int LegalEntityId,
    int PlatformAccountId,
    string WorkerCategory,
    string Code,
    int Version,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    CompensationPolicyStatus Status,
    string RowVersion,
    IReadOnlyCollection<CompensationRuleResponse> Rules);

public record CompensationSimulationComponentResponse(
    Guid RuleId,
    string RuleCode,
    string RuleName,
    CompensationComponentType ComponentType,
    decimal Quantity,
    decimal Rate,
    decimal Amount,
    bool Selected,
    string Explanation);

public record CompensationSimulationResponse(
    Guid PolicyId,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyCollection<CompensationSimulationComponentResponse> Components,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal NetAmount,
    IReadOnlyCollection<string> Conflicts);

public class CreateCompensationPolicyRequestValidator : AbstractValidator<CreateCompensationPolicyRequest>
{
    public CreateCompensationPolicyRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.PlatformAccountId).GreaterThan(0);
        RuleFor(x => x.WorkerCategory).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue);
        RuleFor(x => x.Rules).NotEmpty();
        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
            rule.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            rule.RuleFor(x => x.MetricCode).NotEmpty().MaximumLength(64);
            rule.RuleFor(x => x.RoundingScale).InclusiveBetween(0, 4);
        });
    }
}

public class CloneCompensationPolicyVersionRequestValidator : AbstractValidator<CloneCompensationPolicyVersionRequest>
{
    public CloneCompensationPolicyVersionRequestValidator()
    {
        RuleFor(x => x.EffectiveTo)
            .GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue);
    }
}
