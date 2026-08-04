namespace Unifi.Mcp.Client;

public interface IUniFiApiClientFactory : IDisposable
{
    IReadOnlyCollection<string> ProfileNames { get; }

    IUniFiApiClient Create(string profileName);
}
