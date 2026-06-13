using System.Text.Json;
using FluentValidation;

namespace laundryghar.Catalog.Application.Catalog;

/// <summary>
/// Reusable FluentValidation rules for string properties that map to jsonb columns.
/// Guards against malformed JSON reaching Postgres (which would surface as a raw 22P02
/// error) by failing validation with a clean 422 instead.
/// </summary>
public static class JsonValidationExtensions
{
    private const string JsonObjectMessage = "must be a valid JSON object";

    /// <summary>
    /// Passes when the value is null/empty (deferring to any companion NotEmpty rule),
    /// or when it parses as a JSON document whose root is an object.
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustBeJsonObject<T>(this IRuleBuilder<T, string> rb) =>
        rb.Must(IsJsonObjectOrEmpty).WithMessage(JsonObjectMessage);

    private static bool IsJsonObjectOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true; // defer to NotEmpty
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
