using System.Globalization;
using System.Text.Json;

namespace operations.Application.Common.Settings;

/// <summary>
/// The <c>system_settings.setting_value</c> column is <c>jsonb</c>, so every scalar value must be
/// stored as a valid JSON literal — a bare number (<c>18</c>), a JSON string (<c>"INR"</c>), or a
/// boolean (<c>true</c>). This codec is the single place that encodes an operator-supplied string
/// into that jsonb literal on write and decodes the stored literal back to a typed value on read,
/// so producers and consumers can never disagree on the wire format.
/// </summary>
public static class SettingValueCodec
{
    /// <summary>The scalar data-types the API accepts (object/json blobs bypass this codec).</summary>
    public static readonly string[] ScalarDataTypes = ["decimal", "int", "bool", "string"];

    public static bool IsScalar(string dataType) => Array.IndexOf(ScalarDataTypes, dataType) >= 0;

    /// <summary>
    /// Canonicalises a raw operator-supplied value into the jsonb literal to persist.
    /// Throws <see cref="FormatException"/> when the value does not parse as its declared type —
    /// the write path surfaces this as a 422 so a malformed value never reaches the column.
    /// </summary>
    public static string Encode(string rawValue, string dataType)
    {
        rawValue = rawValue?.Trim() ?? throw new FormatException("Value is required.");
        switch (dataType)
        {
            case "int":
                if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    throw new FormatException($"'{rawValue}' is not a valid integer.");
                return l.ToString(CultureInfo.InvariantCulture);

            case "decimal":
                if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"'{rawValue}' is not a valid number.");
                return d.ToString(CultureInfo.InvariantCulture);

            case "bool":
                if (!bool.TryParse(rawValue, out var b))
                    throw new FormatException($"'{rawValue}' is not a valid boolean.");
                return b ? "true" : "false";

            case "string":
                // JSON-encode so quotes/backslashes/unicode are escaped → always valid jsonb.
                return JsonSerializer.Serialize(rawValue);

            default:
                throw new FormatException($"Unsupported scalar data type '{dataType}'.");
        }
    }

    /// <summary>Decodes a stored jsonb scalar literal to a plain display string (unquotes strings).</summary>
    public static string DecodeString(string jsonbValue)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonbValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString() ?? "",
                JsonValueKind.Number => doc.RootElement.GetRawText(),
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                _                    => jsonbValue,
            };
        }
        catch (JsonException)
        {
            // Legacy/non-JSON value stored directly — return verbatim rather than throw.
            return jsonbValue.Trim('"');
        }
    }

    public static decimal? TryDecimal(string jsonbValue)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonbValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Number => doc.RootElement.GetDecimal(),
                JsonValueKind.String => decimal.TryParse(doc.RootElement.GetString(),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : null,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return decimal.TryParse(jsonbValue.Trim('"'),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
    }

    public static int? TryInt(string jsonbValue)
    {
        var d = TryDecimal(jsonbValue);
        return d is null ? null : (int)decimal.Truncate(d.Value);
    }

    public static bool? TryBool(string jsonbValue)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonbValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.True  => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(doc.RootElement.GetString(), out var s) ? s : null,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return bool.TryParse(jsonbValue.Trim('"'), out var v) ? v : null;
        }
    }
}
