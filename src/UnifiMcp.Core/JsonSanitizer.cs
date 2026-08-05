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
        "ipaddress",
        "mac",
        "macaddress",
        "serial",
        "serialnumber",
        "email",
        "hostname",
        "hardwareid"
    };

    public static string Summarize(
        string? payload,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength,
        int maxOutputCharacters,
        int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "Empty response.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var budget = new SanitizationBudget(maxOutputCharacters, maxDepth);
            var node = Sanitize(document.RootElement, maxCollectionItems, maxObjectProperties, maxStringLength, budget, 0);
            var result = node?.ToJsonString() ?? "null";
            return result.Length <= maxOutputCharacters ? result : "\"[output truncated]\"";
        }
        catch (JsonException)
        {
            return $"[non-JSON response omitted; {Encoding.UTF8.GetByteCount(payload)} bytes]";
        }
    }

    public static JsonElement SanitizeToElement(
        string? payload,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength,
        int maxOutputCharacters,
        int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var budget = new SanitizationBudget(maxOutputCharacters, maxDepth);
            var node = Sanitize(document.RootElement, maxCollectionItems, maxObjectProperties, maxStringLength, budget, 0);
            var result = JsonSerializer.SerializeToElement(node);
            return result.GetRawText().Length <= maxOutputCharacters
                ? result
                : JsonSerializer.SerializeToElement("[output truncated]");
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new
            {
                omitted = true,
                reason = "non-json",
                byteCount = Encoding.UTF8.GetByteCount(payload)
            });
        }
    }

    private static JsonNode? Sanitize(
        JsonElement element,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength,
        SanitizationBudget budget,
        int depth)
    {
        if (depth >= budget.MaxDepth || !budget.TryConsume(4))
        {
            return JsonValue.Create("[truncated]");
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(element, maxCollectionItems, maxObjectProperties, maxStringLength, budget, depth),
            JsonValueKind.Array => SanitizeArray(element, maxCollectionItems, maxObjectProperties, maxStringLength, budget, depth),
            JsonValueKind.String => JsonValue.Create(TruncateAndConsume(element.GetString(), maxStringLength, budget)),
            JsonValueKind.Number => SanitizeNumber(element, budget),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => JsonValue.Create(TruncateAndConsume(element.ToString(), maxStringLength, budget))
        };
    }

    private static JsonObject SanitizeObject(
        JsonElement element,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength,
        SanitizationBudget budget,
        int depth)
    {
        var result = new JsonObject();
        var properties = element.EnumerateObject().Take(maxObjectProperties).ToArray();

        foreach (var property in properties)
        {
            if (!budget.TryConsume(property.Name.Length + 6))
            {
                result["_outputTruncated"] = true;
                break;
            }

            if (IsSensitiveProperty(property.Name))
            {
                if (!budget.TryConsume(12))
                {
                    result["_outputTruncated"] = true;
                    break;
                }

                result[property.Name] = "[redacted]";
                continue;
            }

            var child = Sanitize(property.Value, maxCollectionItems, maxObjectProperties, maxStringLength, budget, depth + 1);
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

    private static JsonArray SanitizeArray(
        JsonElement element,
        int maxCollectionItems,
        int maxObjectProperties,
        int maxStringLength,
        SanitizationBudget budget,
        int depth)
    {
        var result = new JsonArray();
        var items = element.EnumerateArray().Take(maxCollectionItems).ToArray();

        foreach (var item in items)
        {
            if (!budget.TryConsume(2))
            {
                result.Add("_outputTruncated");
                break;
            }

            var child = Sanitize(item, maxCollectionItems, maxObjectProperties, maxStringLength, budget, depth + 1);
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

    private static JsonNode? SanitizeNumber(JsonElement element, SanitizationBudget budget)
    {
        var rawValue = element.GetRawText();
        return budget.TryConsume(rawValue.Length)
            ? JsonNode.Parse(rawValue)
            : JsonValue.Create("[truncated]");
    }

    private static string TruncateAndConsume(string? value, int maxStringLength, SanitizationBudget budget)
    {
        var truncated = Truncate(value, Math.Min(maxStringLength, Math.Max(0, budget.RemainingCharacters)));
        budget.TryConsume(truncated.Length + 2);
        return truncated;
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var normalized = new string(propertyName.Where(char.IsLetterOrDigit).ToArray());
        var containsSecretMarker =
            normalized.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("pwd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("psk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("auth", StringComparison.OrdinalIgnoreCase);

        return propertyName.StartsWith("x_", StringComparison.OrdinalIgnoreCase) ||
               SensitivePropertyNames.Contains(normalized) ||
               containsSecretMarker ||
               normalized.StartsWith("ip", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("ip", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.EndsWith("ship", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ipaddrs", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("wanip", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("publicip", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("externalip", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("serialno", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("mac", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("ipaddr", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("ipaddress", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("hostname", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("serialno", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("serialnumber", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SanitizationBudget
    {
        public SanitizationBudget(int maxOutputCharacters, int maxDepth)
        {
            RemainingCharacters = maxOutputCharacters;
            MaxDepth = maxDepth;
        }

        public int RemainingCharacters { get; private set; }

        public int MaxDepth { get; }

        public bool TryConsume(int characters)
        {
            if (characters > RemainingCharacters)
            {
                RemainingCharacters = 0;
                return false;
            }

            RemainingCharacters -= characters;
            return true;
        }
    }
}
