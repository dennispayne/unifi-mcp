namespace Unifi.Mcp.Client;

public interface IUniFiAuthenticator
{
    Task<UniFiSessionToken> AuthenticateAsync(
        UniFiAccessProfileOptions profile,
        IUniFiTransport transport,
        CancellationToken cancellationToken = default);
}
