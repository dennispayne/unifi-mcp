namespace Unifi.Mcp.Client;

internal static class SetCookieParser
{
    public static IReadOnlyDictionary<string, string> Parse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("Set-Cookie", out var headerValues))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawHeader in headerValues)
        {
            if (string.IsNullOrWhiteSpace(rawHeader))
            {
                continue;
            }

            var pair = rawHeader.Split(';', 2, StringSplitOptions.TrimEntries)[0];
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = pair[..separatorIndex].Trim();
            var value = pair[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                cookies[name] = value;
            }
        }

        return cookies;
    }
}
