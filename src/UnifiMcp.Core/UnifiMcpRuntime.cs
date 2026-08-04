using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

public sealed class UnifiMcpRuntime : IDisposable
{
    private readonly UniFiApiClientFactory _clientFactory;

    public UnifiMcpRuntime(UnifiMcpConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _clientFactory = new UniFiApiClientFactory(configuration.ToClientOptions());
        Server = new UnifiMcpServer(_clientFactory, configuration.Server);
        Host = new McpJsonRpcHost(Server);
    }

    public UnifiMcpConfiguration Configuration { get; }

    public UnifiMcpServer Server { get; }

    public McpJsonRpcHost Host { get; }

    public void Dispose() => _clientFactory.Dispose();
}

public static class UnifiMcpRuntimeLoader
{
    public static UnifiMcpRuntime LoadFromPath(string path) =>
        new(UnifiMcpConfigurationLoader.LoadFromFile(path));

    public static string ResolveConfigPath(string? explicitPath = null)
    {
        foreach (var candidate in EnumerateCandidatePaths(explicitPath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "UniFi MCP configuration file was not found. Copy config\\unifi-mcp.settings.example.json to config\\unifi-mcp.settings.json or set UNIFI_MCP_CONFIG.");
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return Path.GetFullPath(explicitPath);
        }

        var environmentPath = Environment.GetEnvironmentVariable("UNIFI_MCP_CONFIG");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            yield return Path.GetFullPath(environmentPath);
        }

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Environment.CurrentDirectory),
            Path.GetFullPath(AppContext.BaseDirectory)
        };

        foreach (var root in roots)
        {
            yield return Path.Combine(root, "config", "unifi-mcp.settings.json");
            yield return Path.Combine(root, "unifi-mcp.settings.json");
        }
    }

}
