using System.Text.Json;

namespace Unifi.Mcp.Client;

internal static class JsonSearch
{
    public static bool TryFindString(JsonElement? root, IReadOnlyCollection<string> propertyNames, out string? value)
    {
        if (TryFindValue(root, propertyNames, out var element))
        {
            value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };

            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    public static bool TryFindInt32(JsonElement? root, IReadOnlyCollection<string> propertyNames, out int value)
    {
        if (TryFindValue(root, propertyNames, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryFindValue(JsonElement? root, IReadOnlyCollection<string> propertyNames, out JsonElement element)
    {
        if (root is null)
        {
            element = default;
            return false;
        }

        var stack = new Stack<JsonElement>();
        stack.Push(root.Value);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.EnumerateObject())
                    {
                        if (propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            element = property.Value;
                            return true;
                        }

                        stack.Push(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in current.EnumerateArray())
                    {
                        stack.Push(item);
                    }

                    break;
            }
        }

        element = default;
        return false;
    }
}
