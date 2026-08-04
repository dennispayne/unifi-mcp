namespace Unifi.Mcp.Client;

public interface IUniFiTransportFactory
{
    IUniFiTransport Create(UniFiAccessProfileOptions profile);
}
