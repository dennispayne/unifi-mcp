using System.Net.Http.Json;

namespace Unifi.Mcp.Client;

public interface IUniFiApiClient : IDisposable
{
    string ProfileName { get; }

    string? ScopeDescription { get; }

    UniFiServiceKind Service { get; }

    bool AllowMutations { get; }

    IReadOnlySet<string> AllowedHttpMethods { get; }

    bool AllowConnectorProxy { get; }

    IReadOnlyList<string> ConnectorAllowedPathPrefixes { get; }

    Task<HttpResponseMessage> SendAsync(UniFiApiRequest request, CancellationToken cancellationToken = default);

    async Task<T?> GetFromJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(UniFiApiRequest.Get(relativePath), cancellationToken).ConfigureAwait(false);
        return response.Content is null
            ? default
            : await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    async Task<TResponse?> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string relativePath,
        TRequest requestBody,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(UniFiApiRequest.FromJson(method, relativePath, requestBody), cancellationToken).ConfigureAwait(false);
        return response.Content is null
            ? default
            : await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken).ConfigureAwait(false);
    }
}
