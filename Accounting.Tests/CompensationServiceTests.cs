using Application.Service.Compensation;
using Domain.Entities.AccountingPlatform;
using Xunit;

namespace Accounting.Tests;

public class CompensationServiceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(499, 1996)]
    [InlineData(500, 2000)]
    [InlineData(501, 2006)]
    [InlineData(600, 2600)]
    public void HungerTier_IsConfiguredAndEvaluatedAtBoundaries(decimal orders, decimal expected)
    {
        var policy = Policy(
            Rule("HUNGER_ORDERS", CompensationRuleTemplate.TieredBasePlusExcess, rate: null, threshold: 500m, belowRate: 4m, aboveRate: 6m, baseAmount: 2000m));

        var result = CompensationService.Evaluate(policy, new Dictionary<string, decimal> { ["ACCEPTED_ORDERS"] = orders });

        Assert.Equal(expected, result.TotalEarnings);
        Assert.Equal(expected, result.NetAmount);
    }

    [Fact]
    public void AmazonFixedAmount_DoesNotDependOnOrders()
    {
        var policy = Policy(Rule("AMAZON_FIXED", CompensationRuleTemplate.FixedAmount, fixedAmount: 2000m));

        var result = CompensationService.Evaluate(policy, new Dictionary<string, decimal> { ["ACCEPTED_ORDERS"] = 9999m });

        Assert.Equal(2000m, result.TotalEarnings);
    }

    [Fact]
    public void KeetaThresholdBonus_AppliesAtSixHundredOrders()
    {
        var policy = Policy(Rule("KEETA_600", CompensationRuleTemplate.Threshold, fixedAmount: 500m, threshold: 600m, conditionOperator: ">="));

        var below = CompensationService.Evaluate(policy, new Dictionary<string, decimal> { ["ACCEPTED_ORDERS"] = 599m });
        var at = CompensationService.Evaluate(policy, new Dictionary<string, decimal> { ["ACCEPTED_ORDERS"] = 600m });

        Assert.Equal(0m, below.TotalEarnings);
        Assert.Equal(500m, at.TotalEarnings);
    }

    [Fact]
    public void ExclusiveGroup_SelectsHighestEligibleBonus()
    {
        var first = Rule("BONUS_A", CompensationRuleTemplate.Threshold, fixedAmount: 300m, threshold: 500m, conditionOperator: ">=");
        first.ExclusiveGroup = "MONTHLY_BONUS";
        var second = Rule("BONUS_B", CompensationRuleTemplate.Threshold, fixedAmount: 500m, threshold: 600m, conditionOperator: ">=");
        second.ExclusiveGroup = "MONTHLY_BONUS";
        var policy = Policy(first, second);

        var result = CompensationService.Evaluate(policy, new Dictionary<string, decimal> { ["ACCEPTED_ORDERS"] = 600m });

        Assert.Equal(500m, result.TotalEarnings);
        Assert.Single(result.Components, x => x.Selected);
    }

    private static CompensationPolicyVersion Policy(params CompensationRule[] rules) => new()
    {
        Id = Guid.NewGuid(),
        Rules = rules
    };

    private static CompensationRule Rule(
        string code,
        CompensationRuleTemplate template,
        decimal? fixedAmount = null,
        decimal? rate = null,
        decimal? threshold = null,
        decimal? belowRate = null,
        decimal? aboveRate = null,
        decimal? baseAmount = null,
        string? conditionOperator = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Template = template,
        ComponentType = CompensationComponentType.Earning,
        MetricCode = "ACCEPTED_ORDERS",
        FixedAmount = fixedAmount,
        Rate = rate,
        ConditionValue = threshold,
        BelowRate = belowRate,
        AboveRate = aboveRate,
        BaseAmount = baseAmount,
        ConditionOperator = conditionOperator,
        StackingMode = CompensationStackingMode.ExclusiveHighest,
        RoundingScale = 2,
        IsActive = true
    };
}
