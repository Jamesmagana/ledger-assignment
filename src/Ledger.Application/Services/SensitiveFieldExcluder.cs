using System.Text.Json;

namespace Ledger.Application.Services;

/// <summary>
/// Utility class for excluding sensitive fields from JSON serialization.
/// </summary>
public static class SensitiveFieldExcluder
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "Password",
        "Secret",
        "SecretKey",
        "Token",
        "ApiKey",
        "AccessToken",
        "RefreshToken"
    };

    /// <summary>
    /// Serializes an object to a dictionary, excluding sensitive fields.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>A dictionary containing non-sensitive properties.</returns>
    public static Dictionary<string, object?> SerializeExcludingSensitive(object? obj)
    {
        if (obj == null)
        {
            return new Dictionary<string, object?>();
        }

        // Serialize to JSON first, then deserialize to dictionary
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);

        if (dict == null)
        {
            return new Dictionary<string, object?>();
        }

        // Filter out sensitive fields
        return dict
            .Where(kvp => !SensitiveFields.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Serializes an object to JSON string, excluding sensitive fields.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>JSON string with sensitive fields excluded, or null if object is null.</returns>
    public static string? SerializeToJsonExcludingSensitive(object? obj)
    {
        if (obj == null)
        {
            return null;
        }

        var dict = SerializeExcludingSensitive(obj);

        if (dict.Count == 0)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(dict, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

