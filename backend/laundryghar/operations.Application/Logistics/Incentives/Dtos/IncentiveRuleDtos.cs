namespace operations.Application.Logistics.Incentives.Dtos;

/// <summary>Admin view of a rider incentive rule. JSON is camelCase.</summary>
public sealed record IncentiveRuleDto(
    Guid            Id,
    string          Name,
    string          RuleType,
    int             Threshold,
    decimal         RewardAmount,
    string          Window,
    bool            IsActive,
    DateTimeOffset  ValidFrom,
    DateTimeOffset? ValidUntil);

public sealed record CreateIncentiveRuleRequest(
    string          Name,
    string          RuleType,
    int             Threshold,
    decimal         RewardAmount,
    bool?           IsActive,
    DateTimeOffset? ValidUntil);

public sealed record UpdateIncentiveRuleRequest(
    string          Name,
    string          RuleType,
    int             Threshold,
    decimal         RewardAmount,
    bool?           IsActive,
    DateTimeOffset? ValidUntil);
