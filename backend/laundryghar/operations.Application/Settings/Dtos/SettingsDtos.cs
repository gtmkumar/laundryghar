namespace operations.Application.Settings.Dtos;

/// <summary>A raw stored setting row visible to the caller (one scope level's value for a key).</summary>
public sealed record SettingRowDto(
    Guid Id,
    string Category,
    string Key,
    string ScopeType,
    Guid? FranchiseId,
    Guid? StoreId,
    string Value,
    string DataType,
    string? ValidationSchema,
    int Version,
    DateTimeOffset UpdatedAt);

/// <summary>The value that actually applies for a key at the requested scope, after
/// store→franchise→brand→platform precedence, plus which scope supplied it.</summary>
public sealed record EffectiveSettingDto(
    string Key,
    string Value,
    string DataType,
    string SourceScope);

/// <summary>List response: the raw rows the caller can see for a category plus the effective
/// value per key for the requested (franchise/store) target scope.</summary>
public sealed record SettingsListDto(
    IReadOnlyList<SettingRowDto> Rows,
    IReadOnlyList<EffectiveSettingDto> Effective);

/// <summary>PUT body. <c>Value == null</c> clears (deletes) this scope's row for the key.
/// <c>ValidationSchema</c> is only honoured for brand-scope writes (HQ sets the clamp band).</summary>
public sealed record UpsertSettingRequest(
    string Category,
    string Key,
    string ScopeType,
    Guid? FranchiseId,
    Guid? StoreId,
    string? Value,
    string DataType,
    string? ValidationSchema);
