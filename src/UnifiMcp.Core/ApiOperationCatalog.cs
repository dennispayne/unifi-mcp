using System.Reflection;
using System.Text.Json;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

internal sealed record ApiOperation(
    UniFiServiceKind Service,
    string Version,
    string Method,
    string Path,
    string? OperationId,
    string? Summary,
    IReadOnlyList<string> Tags,
    bool HasRequestBody,
    bool RequestBodyRequired,
    bool Mutating);

internal static class ApiOperationCatalog
{
    private static readonly Lazy<IReadOnlyList<ApiOperation>> Operations = new(Load);

    public static IReadOnlyList<ApiOperation> GetAll() => Operations.Value;

    private static IReadOnlyList<ApiOperation> Load()
    {
        var assembly = typeof(ApiOperationCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("ApiContracts.operations.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded UniFi API operation catalog was not found.");
        using var document = JsonDocument.Parse(stream);
        var operations = new List<ApiOperation>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            operations.Add(new ApiOperation(
                ParseService(item.GetProperty("service").GetString()),
                item.GetProperty("version").GetString() ?? string.Empty,
                item.GetProperty("method").GetString() ?? string.Empty,
                item.GetProperty("path").GetString() ?? string.Empty,
                ReadOptionalString(item, "operationId"),
                ReadOptionalString(item, "summary"),
                item.GetProperty("tags").EnumerateArray()
                    .Select(static tag => tag.GetString())
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Cast<string>()
                    .ToArray(),
                item.GetProperty("hasRequestBody").GetBoolean(),
                item.GetProperty("requestBodyRequired").GetBoolean(),
                item.GetProperty("mutating").GetBoolean()));
        }

        return operations;
    }

    private static UniFiServiceKind ParseService(string? service) =>
        service switch
        {
            "siteManager" => UniFiServiceKind.SiteManager,
            "network" => UniFiServiceKind.Network,
            _ => throw new InvalidOperationException($"Unknown API catalog service '{service}'.")
        };

    private static string? ReadOptionalString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
