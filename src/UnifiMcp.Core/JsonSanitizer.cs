using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiMcp.Core;

internal static class JsonSanitizer
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "cookie",
        "set-cookie",
        "x-csrf-token",
        "token",
        "accessToken",
        "refreshToken",
        "password",
        "secret",
        "clientSecret",
        "privateKey",
        "session",
        "sid",
        "ip",
        "mac",
        "serial",
        "email",
        "hostname"
    };

    public static string Summarize(string? payload, int maxCollectionItems, int maxObjectProperties, int maxStringLength)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "Empty response.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var node = Sanitize(document.RootElement, maxCollectionItems, maxObjectProperties, maxStringLength);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
        }
        catch (JsonException)
        {
            return Truncate(payload, maxStringLength);
        }
    }

    public static string SummarizeResponse(
        HttpResponseMessage response,
        string payload,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength)
    {
        var builder = new StringBuilder();
        builder.Append("status=").Append((int)response.StatusCode);

        if (response.Content?.Headers.ContentType is not null)
        {
            builder.Append(", contentType=").Append(response.Content.Headers.ContentType.MediaType);
        }

        if (!string.IsNullOrWhiteSpace(payload))
        {
            builder.Append(", body=").Append(Summarize(payload, maxCollectionItems, maxObjectProperties, maxStringLength));
        }

        return builder.ToString();
    }

    private static JsonNode? Sanitize(JsonElement element, int maxCollectionItems, int maxObjectProperties, int maxStringLength)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(element, maxCollectionItems, maxObjectProperties, maxStringLength),
            JsonValueKind.Array => SanitizeArray(element, maxCollectionItems, maxObjectProperties, maxStringLength),
            JsonValueKind.String => JsonValue.Create(Truncate(element.GetString(), maxStringLength)),
            JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => JsonValue.Create(Truncate(element.ToString(), maxStringLength))
        };
    }

    private static JsonObject SanitizeObject(JsonElement element, int maxCollectionItems, int maxObjectProperties, int maxStringLength)
    {
        var result = new JsonObject();
        var properties = element.EnumerateObject().Take(maxObjectProperties).ToArray();

        foreach (var property in properties)
        {
            if (SensitivePropertyNames.Contains(property.Name))
            {
                result[property.Name] = "[redacted]";
                continue;
            }

            var child = Sanitize(property.Value, maxCollectionItems, maxObjectProperties, maxStringLength);
            if (child is not null)
            {
                result[property.Name] = child;
            }
        }

        if (element.EnumerateObject().Count() > properties.Length)
        {
            result["_truncated"] = true;
        }

        return result;
    }

    private static JsonArray SanitizeArray(JsonElement element, int maxCollectionItems, int maxObjectProperties, int maxStringLength)
    {
        var result = new JsonArray();
        var items = element.EnumerateArray().Take(maxCollectionItems).ToArray();

        foreach (var item in items)
        {
            var child = Sanitize(item, maxCollectionItems, maxObjectProperties, maxStringLength);
            if (child is not null)
            {
                result.Add(child);
            }
        }

        if (element.GetArrayLength() > items.Length)
        {
            result.Add("_truncated");
        }

        return result;
    }

    private static string Truncate(string? value, int maxStringLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxStringLength ? value : value[..maxStringLength] + "...";
    }
}
