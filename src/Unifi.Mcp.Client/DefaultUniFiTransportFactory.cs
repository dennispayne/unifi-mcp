namespace Unifi.Mcp.Client;

public sealed class DefaultUniFiTransportFactory : IUniFiTransportFactory
{
    public IUniFiTransport Create(UniFiAccessProfileOptions profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new HttpClientUniFiTransport(profile.BaseAddress);
    }
}

internal sealed class HttpClientUniFiTransport : IUniFiTransport
{
    private readonly HttpClient _httpClient;

    public HttpClientUniFiTransport(Uri baseAddress)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(100)
        };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}
