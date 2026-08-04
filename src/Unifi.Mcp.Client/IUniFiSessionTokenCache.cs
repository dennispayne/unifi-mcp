namespace Unifi.Mcp.Client;

public interface IUniFiSessionTokenCache
{
    Task<UniFiSessionToken> GetOrCreateAsync(
        UniFiAccessProfileOptions profile,
        Func<CancellationToken, Task<UniFiSessionToken>> tokenFactory,
        TimeSpan refreshSkew,
        CancellationToken cancellationToken = default);

    void Invalidate(string profileName);
}
