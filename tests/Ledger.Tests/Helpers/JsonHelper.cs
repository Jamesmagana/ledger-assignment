using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ledger.Tests.Helpers;

/// <summary>
/// Helper for JSON serialization with the same options as the API.
/// </summary>
public static class JsonHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Gets the JSON serializer options matching the API configuration.
    /// </summary>
    public static JsonSerializerOptions GetJsonOptions() => _jsonOptions;

    /// <summary>
    /// Reads and deserializes JSON content using the API's JSON options.
    /// </summary>
    public static async Task<T?> ReadFromJsonAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        return await content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
    }
}
