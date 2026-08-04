using System.Reflection;

namespace UnifiMcp.Core;

public sealed class UnifiMcpServerOptions
{
    public string Name { get; init; } = "unifi-mcp";

    public string Version { get; init; } = GetAssemblyVersion();

    public string ProtocolVersion { get; init; } = "2024-11-05";

    public bool AllowRawResponses { get; init; }

    public int MaxCollectionItems { get; init; } = 25;

    public int MaxObjectProperties { get; init; } = 12;

    public int MaxStringLength { get; init; } = 256;

    public int MaxUpstreamResponseBytes { get; init; } = 1024 * 1024;

    public int MaxStdioMessageBytes { get; init; } = 1024 * 1024;

    public int MaxSanitizedOutputCharacters { get; init; } = 32 * 1024;

    public int MaxJsonDepth { get; init; } = 12;

    public int MaxRequestBodyBytes { get; init; } = 256 * 1024;

    public string MutationApprovalKeyEnvironmentVariable { get; init; } = "UNIFI_MCP_MUTATION_APPROVAL_KEY";

    public int MutationApprovalMaxAgeSeconds { get; init; } = 300;

    public int MaxOperationSchemaCharacters { get; init; } = 128 * 1024;

    private static string GetAssemblyVersion()
    {
        var informationalVersion = typeof(UnifiMcpServerOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? "0.0.0"
            : informationalVersion.Split('+', 2)[0];
    }

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

        if (MaxUpstreamResponseBytes <= 0)
        {
            throw new InvalidOperationException("MaxUpstreamResponseBytes must be positive.");
        }

        if (MaxStdioMessageBytes <= 0)
        {
            throw new InvalidOperationException("MaxStdioMessageBytes must be positive.");
        }

        if (MaxSanitizedOutputCharacters < 64)
        {
            throw new InvalidOperationException("MaxSanitizedOutputCharacters must be at least 64.");
        }

        if (MaxJsonDepth <= 0)
        {
            throw new InvalidOperationException("MaxJsonDepth must be positive.");
        }

        if (MaxRequestBodyBytes <= 0)
        {
            throw new InvalidOperationException("MaxRequestBodyBytes must be positive.");
        }

        if (string.IsNullOrWhiteSpace(MutationApprovalKeyEnvironmentVariable))
        {
            throw new InvalidOperationException("MutationApprovalKeyEnvironmentVariable is required.");
        }

        if (MutationApprovalMaxAgeSeconds is < 30 or > 3600)
        {
            throw new InvalidOperationException("MutationApprovalMaxAgeSeconds must be from 30 through 3600.");
        }

        if (MaxOperationSchemaCharacters < 1024)
        {
            throw new InvalidOperationException("MaxOperationSchemaCharacters must be at least 1024.");
        }
    }
}
