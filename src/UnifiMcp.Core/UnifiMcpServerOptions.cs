namespace UnifiMcp.Core;

public sealed class UnifiMcpServerOptions
{
    public string Name { get; init; } = "unifi-mcp";

    public string Version { get; init; } = "0.1.0";

    public string ProtocolVersion { get; init; } = "2024-11-05";

    public bool AllowRawResponses { get; init; }

    public int MaxCollectionItems { get; init; } = 25;

    public int MaxObjectProperties { get; init; } = 12;

    public int MaxStringLength { get; init; } = 256;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Server name is required.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidOperationException("Server version is required.");
        }

        if (string.IsNullOrWhiteSpace(ProtocolVersion))
        {
            throw new InvalidOperationException("ProtocolVersion is required.");
        }

        if (MaxCollectionItems <= 0)
        {
            throw new InvalidOperationException("MaxCollectionItems must be positive.");
        }

        if (MaxObjectProperties <= 0)
        {
            throw new InvalidOperationException("MaxObjectProperties must be positive.");
        }

        if (MaxStringLength <= 0)
        {
            throw new InvalidOperationException("MaxStringLength must be positive.");
        }
    }
}
