using System.Net.Http.Headers;

namespace Unifi.Mcp.Client;

public sealed class UniFiSessionToken
{
    public required DateTimeOffset ExpiresAt { get; init; }

    public IReadOnlyDictionary<string, string> Cookies { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? BearerToken { get; init; }

    public string? CsrfToken { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal bool IsExpired(DateTimeOffset now, TimeSpan refreshSkew) => ExpiresAt <= now.Add(refreshSkew);

    internal void Apply(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
        }

        if (!string.IsNullOrWhiteSpace(CsrfToken))
        {
            request.Headers.TryAddWithoutValidation("X-CSRF-Token", CsrfToken);
        }

        foreach (var header in Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (Cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                string.Join("; ", Cookies.Select(static cookie => $"{cookie.Key}={cookie.Value}")));
        }
    }
}
