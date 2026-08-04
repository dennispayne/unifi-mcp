namespace UnifiMcp.Core;

public sealed class UnifiMcpRuntimeOptions
{
    public string ProfilesPath { get; set; } = Path.Combine("config", "unifi-profiles.json");

    public string HttpPath { get; set; } = "/mcp";

    public string HealthPath { get; set; } = "/health";

    public string? HttpBearerToken { get; set; }

    public long MaxHttpRequestBodySize { get; set; } = 64 * 1024;

    public UnifiMcpServerOptions Server { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfilesPath))
        {
            throw new InvalidOperationException("A UniFi profile configuration path is required.");
        }

        HttpPath = NormalizeHttpPath(HttpPath);
        HealthPath = NormalizeHttpPath(HealthPath);
        HttpBearerToken = string.IsNullOrWhiteSpace(HttpBearerToken) ? null : HttpBearerToken.Trim();

        if (string.Equals(HttpPath, HealthPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTTP MCP path and health path must be different.");
        }

        if (MaxHttpRequestBodySize <= 0)
        {
            throw new InvalidOperationException("MaxHttpRequestBodySize must be positive.");
        }

        Server.Validate();
    }

    private static string NormalizeHttpPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("HTTP paths are required.");
        }

        var normalized = value.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }
}
