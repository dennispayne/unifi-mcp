using System.Text.Json;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

public sealed class UnifiMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] DescribeScopeRequiredProperties = ["scope"];
    private static readonly string[] ReadRequiredProperties = ["scope", "path"];

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        BuildTool(
            "unifi.scopes.list",
            "List configured UniFi scopes without exposing credentials.",
            new
            {
                type = "object",
                properties = new { },
                additionalProperties = false
            }),
        BuildTool(
            "unifi.scopes.get",
            "Describe one configured UniFi scope, including its read-only boundaries.",
            new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Configured scope name." }
                },
                required = DescribeScopeRequiredProperties,
                additionalProperties = false
            }),
        BuildTool(
            "unifi.scope.read",
            "Read a UniFi API path through one configured scope and return a redacted summary-first result.",
            new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Configured scope name." },
                    path = new { type = "string", description = "Relative UniFi API path such as /proxy/network/api/s/default/stat/device." },
                    includeRaw = new { type = "boolean", description = "Return the raw body summary only if allowed by server policy.", @default = false }
                },
                required = ReadRequiredProperties,
                additionalProperties = false
            })
    ];

    private readonly IUniFiApiClientFactory _clientFactory;
    private readonly UnifiMcpServerOptions _options;

    public UnifiMcpServer(IUniFiApiClientFactory clientFactory, UnifiMcpServerOptions? options = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options ?? new UnifiMcpServerOptions();
        _options.Validate();
    }

    public static IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public async Task<McpResponse?> HandleAsync(McpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Jsonrpc, "2.0", StringComparison.Ordinal))
        {
            return request.Id is null
                ? null
                : Error(request.Id, -32600, "Only JSON-RPC 2.0 requests are supported.");
        }

        try
        {
            return request.Method switch
            {
                "initialize" => Ok(request.Id, await HandleInitializeAsync(request.Params, cancellationToken).ConfigureAwait(false)),
                "notifications/initialized" => null,
                "tools/list" => Ok(request.Id, new { tools = GetTools() }),
                "tools/call" => Ok(request.Id, await HandleToolCallAsync(request.Params, cancellationToken).ConfigureAwait(false)),
                "ping" => request.Id is null ? null : Ok(request.Id, new { ok = true }),
                _ => request.Id is null ? null : Error(request.Id, -32601, $"Unknown MCP method '{request.Method}'.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return request.Id is null ? null : Error(request.Id, -32602, exception.Message);
        }
    }

    private Task<McpInitializeResult> HandleInitializeAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var protocolVersion = ReadOptionalString(parameters, "protocolVersion") ?? _options.ProtocolVersion;

        var serverInfo = JsonSerializer.SerializeToElement(new
        {
            name = _options.Name,
            version = _options.Version
        }, JsonOptions);

        var capabilities = JsonSerializer.SerializeToElement(new
        {
            tools = new { listChanged = false }
        }, JsonOptions);

        const string instructions = "Use the scope-aware read tool first. Keep output bounded and avoid requesting raw payloads unless necessary.";
        return Task.FromResult(new McpInitializeResult(protocolVersion, serverInfo, capabilities, instructions));
    }

    private async Task<McpCallToolResult> HandleToolCallAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null)
        {
            throw new InvalidOperationException("Tool call parameters are required.");
        }

        var name = ReadRequiredString(parameters.Value, "name");
        var arguments = parameters.Value.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : default(JsonElement?);

        return name switch
        {
            "unifi.scopes.list" => await ExecuteToolAsync(() => ListScopesAsync(cancellationToken)).ConfigureAwait(false),
            "unifi.scopes.get" => await ExecuteToolAsync(() => DescribeScopeAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.scope.read" => await ExecuteToolAsync(() => ReadAsync(arguments, cancellationToken)).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown UniFi tool '{name}'.")
        };
    }

    private Task<McpCallToolResult> ListScopesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopes = _clientFactory.ProfileNames
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new
            {
                name,
                description = _clientFactory.Create(name).ScopeDescription
            })
            .ToArray();

        var payload = JsonSerializer.SerializeToElement(new
        {
            summary = $"Configured UniFi scopes: {scopes.Length}.",
            scopes
        }, JsonOptions);

        return Task.FromResult(new McpCallToolResult(
            [new McpTextContent("text", $"Configured UniFi scopes: {scopes.Length}.")],
            payload));
    }

    private Task<McpCallToolResult> DescribeScopeAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scopeName = ReadRequiredString(arguments ?? throw new InvalidOperationException("arguments are required."), "scope");
        var client = _clientFactory.Create(scopeName);

        var payload = JsonSerializer.SerializeToElement(new
        {
            scope = client.ProfileName,
            description = client.ScopeDescription,
            note = "Only allowlisted relative paths configured for this scope can be read."
        }, JsonOptions);

        return Task.FromResult(new McpCallToolResult(
            [new McpTextContent("text", $"Scope '{client.ProfileName}' metadata retrieved.")],
            payload));
    }

    private async Task<McpCallToolResult> ReadAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null)
        {
            throw new InvalidOperationException("arguments are required.");
        }

        var scopeName = ReadRequiredString(arguments.Value, "scope");
        var path = ReadRequiredString(arguments.Value, "path");
        var includeRaw = ReadOptionalBoolean(arguments.Value, "includeRaw") && _options.AllowRawResponses;

        var client = _clientFactory.Create(scopeName);
        using var response = await client.SendAsync(UniFiApiRequest.Get(path), cancellationToken).ConfigureAwait(false);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, object?>
        {
            ["scope"] = client.ProfileName,
            ["path"] = path,
            ["statusCode"] = (int)response.StatusCode,
            ["summary"] = JsonSanitizer.SummarizeResponse(
                response,
                responseBody,
                _options.MaxCollectionItems,
                _options.MaxObjectProperties,
                _options.MaxStringLength)
        };

        if (includeRaw)
        {
            payload["raw"] = JsonSanitizer.Summarize(
                responseBody,
                _options.MaxCollectionItems,
                _options.MaxObjectProperties,
                _options.MaxStringLength);
        }

        var structuredContent = JsonSerializer.SerializeToElement(payload, JsonOptions);
        return new McpCallToolResult(
            [new McpTextContent("text", $"UniFi read completed for scope '{client.ProfileName}' with status {(int)response.StatusCode}.")],
            structuredContent);
    }

    private static async Task<McpCallToolResult> ExecuteToolAsync(Func<Task<McpCallToolResult>> callback)
    {
        try
        {
            return await callback().ConfigureAwait(false);
        }
        catch (UniFiClientException exception)
        {
            var payload = JsonSerializer.SerializeToElement(new
            {
                error = exception.Message,
                scope = exception.ProfileName,
                path = exception.RelativePath,
                statusCode = exception.StatusCode is null ? null : (int?)exception.StatusCode.Value,
                retryable = exception.Retryable
            }, JsonOptions);

            return new McpCallToolResult(
                [new McpTextContent("text", exception.Message)],
                payload,
                IsError: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            var payload = JsonSerializer.SerializeToElement(new
            {
                error = exception.Message
            }, JsonOptions);

            return new McpCallToolResult(
                [new McpTextContent("text", exception.Message)],
                payload,
                IsError: true);
        }
    }

    private static McpToolDescriptor BuildTool(string name, string description, object inputSchema)
    {
        var schema = JsonSerializer.SerializeToElement(inputSchema, JsonOptions);
        return new McpToolDescriptor(name, description, schema);
    }

    private static string ReadRequiredString(JsonElement parameters, string propertyName)
    {
        if (parameters.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Missing required string parameter '{propertyName}'.");
    }

    private static string? ReadOptionalString(JsonElement? parameters, string propertyName)
    {
        if (parameters is null)
        {
            return null;
        }

        return parameters.Value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadOptionalBoolean(JsonElement parameters, string propertyName)
    {
        return parameters.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static McpResponse Ok(JsonElement? id, object result)
    {
        var resultElement = JsonSerializer.SerializeToElement(result, JsonOptions);
        return new McpResponse("2.0", id, resultElement, null);
    }

    private static McpResponse Error(JsonElement? id, int code, string message)
    {
        var error = new McpError(code, message);
        return new McpResponse("2.0", id, null, error);
    }
}
