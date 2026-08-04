namespace Unifi.Mcp.Client;

public interface IUniFiTransport : IDisposable
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
