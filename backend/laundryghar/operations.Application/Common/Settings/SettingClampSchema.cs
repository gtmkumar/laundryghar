using System.Text.Json;
using System.Text.Json.Serialization;

namespace operations.Application.Common.Settings;

/// <summary>
/// A brand-scope setting row may carry a <c>validation_schema</c> that bounds what a
/// franchise/store operator is allowed to override the same key to. Shape (all optional):
/// <c>{ "min": number?, "max": number?, "allowed": string[]? }</c>.
/// When no schema is set the lower scopes may write any value (free).
/// </summary>
public sealed class SettingClampSchema
{
    [JsonPropertyName("min")]     public decimal? Min { get; init; }
    [JsonPropertyName("max")]     public decimal? Max { get; init; }
    [JsonPropertyName("allowed")] public string[]? Allowed { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Parses the brand row's validation_schema jsonb. Returns null when absent/blank/invalid.</summary>
    public static SettingClampSchema? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SettingClampSchema>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Validates a lower-scope write against this schema. Returns <c>null</c> when the value is
    /// allowed, otherwise a human-readable reason. A null schema always passes (no clamp = free).
    /// </summary>
    public static string? Validate(SettingClampSchema? schema, string rawValue, string dataType)
    {
        if (schema is null) return null;

        if (schema.Allowed is { Length: > 0 })
        {
            var decoded = SettingValueCodec.IsScalar(dataType)
                ? SettingValueCodec.DecodeString(TryEncode(rawValue, dataType))
                : rawValue;
            if (Array.IndexOf(schema.Allowed, decoded) < 0)
                return $"Value must be one of: {string.Join(", ", schema.Allowed)}.";
        }

        if (schema.Min.HasValue || schema.Max.HasValue)
        {
            var num = SettingValueCodec.TryDecimal(TryEncode(rawValue, dataType));
            if (num is null)
                return "Value must be numeric to satisfy the min/max constraint.";
            if (schema.Min.HasValue && num < schema.Min.Value)
                return $"Value must be at least {schema.Min.Value}.";
            if (schema.Max.HasValue && num > schema.Max.Value)
                return $"Value must be at most {schema.Max.Value}.";
        }

        return null;
    }

    // Encode may throw on a malformed value; the caller validates format separately, so here we
    // degrade to the raw string (the numeric/allowed checks then fail cleanly rather than 500).
    private static string TryEncode(string rawValue, string dataType)
    {
        try { return SettingValueCodec.IsScalar(dataType) ? SettingValueCodec.Encode(rawValue, dataType) : rawValue; }
        catch (FormatException) { return rawValue; }
    }
}
