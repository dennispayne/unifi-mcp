using System.Text.Json;
using System.Text.RegularExpressions;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

public sealed class UnifiMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] ServiceNames = ["siteManager", "network", "protect", "access", "mobility"];

    private static readonly string[] DescribeScopeRequiredProperties = ["scope"];
    private static readonly string[] ReadRequiredProperties = ["scope", "path"];
    private static readonly string[] ApiRequestRequiredProperties = ["scope", "method", "path"];
    private static readonly string[] SiteIdRequiredProperties = ["scope", "siteId"];
    private static readonly string[] DeviceStatisticsRequiredProperties = ["scope", "siteId", "deviceId"];
    private static readonly string[] WorkspaceIdRequiredProperties = ["scope", "workspaceId"];

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
            }),
        BuildTool(
            "unifi.api.operations.list",
            "Discover official Site Manager, Network, Protect, Access, and Mobility API operations, including mutation requirements.",
            new
            {
                type = "object",
                properties = new
                {
                    service = new { type = "string", @enum = ServiceNames },
                    method = new { type = "string", @enum = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" } },
                    search = new { type = "string", description = "Optional path, operation ID, summary, or tag search." },
                    offset = new { type = "integer", minimum = 0, @default = 0 },
                    limit = new { type = "integer", minimum = 1, maximum = 100, @default = 25 }
                },
                additionalProperties = false
            }),
        BuildTool(
            "unifi.api.operation.get",
            "Get parameters, request-body schema, and referenced definitions for one official API operation.",
            new
            {
                type = "object",
                properties = new
                {
                    service = new { type = "string", @enum = ServiceNames },
                    operationId = new { type = "string" }
                },
                required = new[] { "service", "operationId" },
                additionalProperties = false
            }),
        BuildTool(
            "unifi.api.request",
            "Execute any allowlisted official UniFi API request. Non-GET methods require scope mutation enablement and a one-time request-bound approval token.",
            new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Configured UniFi scope name." },
                    method = new { type = "string", @enum = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" } },
                    path = new { type = "string", description = "Relative API path including an optional query string." },
                    body = new { description = "Optional JSON request body." },
                    mutationApprovalToken = new { type = "string", description = "Short-lived, one-time approval token bound to the exact mutation." },
                    includeRaw = new { type = "boolean", @default = false }
                },
                required = ApiRequestRequiredProperties,
                additionalProperties = false
            }),
        BuildTool(
            "unifi.site_manager.hosts.list",
            "List UniFi hosts through a Site Manager scope. Read-only; bounded to 25 items by default.",
            BuildSiteManagerListSchema()),
        BuildTool(
            "unifi.site_manager.sites.list",
            "List UniFi sites and high-level health statistics through a Site Manager scope. Read-only.",
            BuildSiteManagerListSchema()),
        BuildTool(
            "unifi.site_manager.devices.list",
            "List managed device health through a Site Manager scope. Read-only; identifiers are redacted by policy.",
            BuildSiteManagerDeviceSchema()),
        BuildTool(
            "unifi.site_manager.isp_metrics.get",
            "Get bounded ISP metrics through Site Manager using GET only. Supports 5m or 1h rollups.",
            BuildIspMetricsSchema()),
        BuildTool(
            "unifi.network.info.get",
            "Get the UniFi Network application version for a Network scope. Read-only.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.network.sites.list",
            "List local UniFi Network sites. Read-only and paginated.",
            BuildNetworkListSchema(requireSiteId: false)),
        BuildTool(
            "unifi.network.devices.list",
            "List adopted devices for one Network site. Read-only and paginated.",
            BuildNetworkListSchema(requireSiteId: true)),
        BuildTool(
            "unifi.network.clients.list",
            "List connected clients for one Network site. Read-only, paginated, and identifier-redacted.",
            BuildNetworkListSchema(requireSiteId: true)),
        BuildTool(
            "unifi.network.networks.list",
            "List configured networks for one Network site. Read-only and paginated.",
            BuildNetworkListSchema(requireSiteId: true)),
        BuildTool(
            "unifi.network.wifi.list",
            "List Wi-Fi broadcasts for one Network site. Read-only and paginated.",
            BuildNetworkListSchema(requireSiteId: true)),
        BuildTool(
            "unifi.network.device.statistics.get",
            "Get the latest statistics for one adopted Network device. Read-only.",
            new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Configured Network scope name." },
                    siteId = new { type = "string", format = "uuid" },
                    deviceId = new { type = "string", format = "uuid" }
                },
                required = DeviceStatisticsRequiredProperties,
                additionalProperties = false
            }),
        BuildTool(
            "unifi.protect.info.get",
            "Get the UniFi Protect application information for a Protect scope. Read-only.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.protect.cameras.list",
            "List UniFi Protect cameras. Read-only and identifier-redacted.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.protect.sensors.list",
            "List UniFi Protect sensors. Read-only and identifier-redacted.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.protect.nvrs.get",
            "Get UniFi Protect NVR details. Read-only and identifier-redacted.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.access.doors.list",
            "List UniFi Access doors. Read-only and identifier-redacted.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.access.devices.list",
            "List UniFi Access devices. Read-only and identifier-redacted.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.mobility.workspaces.list",
            "List UniFi Mobility workspaces. Read-only.",
            BuildScopeOnlySchema()),
        BuildTool(
            "unifi.mobility.devices.list",
            "List UniFi Mobility devices for one workspace. Read-only and paginated.",
            new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Configured Mobility scope name." },
                    workspaceId = new { type = "string", format = "uuid" },
                    offset = new { type = "integer", minimum = 0, @default = 0 },
                    limit = new { type = "integer", minimum = 1, maximum = 25, @default = 25 }
                },
                required = WorkspaceIdRequiredProperties,
                additionalProperties = false
            })
    ];

    private readonly IUniFiApiClientFactory _clientFactory;
    private readonly UnifiMcpServerOptions _options;
    private readonly MutationApprovalValidator _mutationApprovalValidator;

    public UnifiMcpServer(IUniFiApiClientFactory clientFactory, UnifiMcpServerOptions? options = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options ?? new UnifiMcpServerOptions();
        _options.Validate();
        _mutationApprovalValidator = new MutationApprovalValidator(
            _options.MutationApprovalKeyEnvironmentVariable,
            _options.MutationApprovalMaxAgeSeconds);
    }

    public static IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public int MaxStdioMessageBytes => _options.MaxStdioMessageBytes;

    public async Task<McpResponse?> HandleAsync(McpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Jsonrpc, "2.0", StringComparison.Ordinal))
        {
            return !request.HasId
                ? null
                : Error(request.Id, -32600, "Only JSON-RPC 2.0 requests are supported.");
        }

        try
        {
            return request.Method switch
            {
                "initialize" => !request.HasId ? null : Ok(request.Id, await HandleInitializeAsync(request.Params, cancellationToken).ConfigureAwait(false)),
                "notifications/initialized" => null,
                "tools/list" => !request.HasId ? null : Ok(request.Id, new { tools = GetTools() }),
                "tools/call" => !request.HasId ? null : Ok(request.Id, await HandleToolCallAsync(request.Params, cancellationToken).ConfigureAwait(false)),
                "ping" => !request.HasId ? null : Ok(request.Id, new { ok = true }),
                _ => !request.HasId ? null : Error(request.Id, -32601, $"Unknown MCP method '{request.Method}'.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return !request.HasId ? null : Error(request.Id, -32602, exception.Message);
        }
    }

    private Task<McpInitializeResult> HandleInitializeAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var protocolVersion = _options.ProtocolVersion;

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
            "unifi.api.operations.list" => await ExecuteToolAsync(() => ListApiOperationsAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.api.operation.get" => await ExecuteToolAsync(() => GetApiOperationAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.api.request" => await ExecuteToolAsync(() => ExecuteApiRequestAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.site_manager.hosts.list" => await ExecuteToolAsync(() => ReadSiteManagerCollectionAsync(arguments, "/v1/hosts", cancellationToken)).ConfigureAwait(false),
            "unifi.site_manager.sites.list" => await ExecuteToolAsync(() => ReadSiteManagerCollectionAsync(arguments, "/v1/sites", cancellationToken)).ConfigureAwait(false),
            "unifi.site_manager.devices.list" => await ExecuteToolAsync(() => ReadSiteManagerDevicesAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.site_manager.isp_metrics.get" => await ExecuteToolAsync(() => ReadSiteManagerIspMetricsAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.network.info.get" => await ExecuteToolAsync(() => ReadNetworkInfoAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.network.sites.list" => await ExecuteToolAsync(() => ReadNetworkCollectionAsync(arguments, "/v1/sites", requireSiteId: false, cancellationToken)).ConfigureAwait(false),
            "unifi.network.devices.list" => await ExecuteToolAsync(() => ReadNetworkCollectionAsync(arguments, "/v1/sites/{siteId}/devices", requireSiteId: true, cancellationToken)).ConfigureAwait(false),
            "unifi.network.clients.list" => await ExecuteToolAsync(() => ReadNetworkCollectionAsync(arguments, "/v1/sites/{siteId}/clients", requireSiteId: true, cancellationToken)).ConfigureAwait(false),
            "unifi.network.networks.list" => await ExecuteToolAsync(() => ReadNetworkCollectionAsync(arguments, "/v1/sites/{siteId}/networks", requireSiteId: true, cancellationToken)).ConfigureAwait(false),
            "unifi.network.wifi.list" => await ExecuteToolAsync(() => ReadNetworkCollectionAsync(arguments, "/v1/sites/{siteId}/wifi/broadcasts", requireSiteId: true, cancellationToken)).ConfigureAwait(false),
            "unifi.network.device.statistics.get" => await ExecuteToolAsync(() => ReadNetworkDeviceStatisticsAsync(arguments, cancellationToken)).ConfigureAwait(false),
            "unifi.protect.info.get" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Protect, "/v1/meta/info", cancellationToken)).ConfigureAwait(false),
            "unifi.protect.cameras.list" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Protect, "/v1/cameras", cancellationToken)).ConfigureAwait(false),
            "unifi.protect.sensors.list" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Protect, "/v1/sensors", cancellationToken)).ConfigureAwait(false),
            "unifi.protect.nvrs.get" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Protect, "/v1/nvrs", cancellationToken)).ConfigureAwait(false),
            "unifi.access.doors.list" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Access, "/api/v1/developer/doors", cancellationToken)).ConfigureAwait(false),
            "unifi.access.devices.list" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Access, "/api/v1/developer/devices", cancellationToken)).ConfigureAwait(false),
            "unifi.mobility.workspaces.list" => await ExecuteToolAsync(() => ReadServiceEndpointAsync(arguments, UniFiServiceKind.Mobility, "/v1/mobility/workspaces", cancellationToken)).ConfigureAwait(false),
            "unifi.mobility.devices.list" => await ExecuteToolAsync(() => ReadMobilityDevicesAsync(arguments, cancellationToken)).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown UniFi tool '{name}'.")
        };
    }

    private Task<McpCallToolResult> GetApiOperationAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = RequireArguments(arguments);
        var service = ParseServiceKind(ReadRequiredString(values, "service"));
        var operationId = ReadRequiredString(values, "operationId");
        if (operationId.Length > 256)
        {
            throw new InvalidOperationException("Parameter 'operationId' must not exceed 256 characters.");
        }

        var details = ApiContractCatalog.GetOperationDetails(service, operationId);
        var payload = JsonSerializer.SerializeToElement(details, JsonOptions);
        if (payload.GetRawText().Length > _options.MaxOperationSchemaCharacters)
        {
            throw new InvalidOperationException(
                $"Operation schema exceeds the {_options.MaxOperationSchemaCharacters}-character limit.");
        }

        return Task.FromResult(new McpCallToolResult(
            [new McpTextContent("text", $"Retrieved schema for {service} operation '{operationId}'.")],
            payload));
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
            allowedHttpMethods = client.AllowedHttpMethods.Order(StringComparer.Ordinal).ToArray(),
            mutationsEnabled = client.AllowMutations,
            note = "Only allowlisted relative paths and HTTP methods configured for this scope can be used."
        }, JsonOptions);

        return Task.FromResult(new McpCallToolResult(
            [new McpTextContent("text", $"Scope '{client.ProfileName}' metadata retrieved.")],
            payload));
    }

    private static Task<McpCallToolResult> ListApiOperationsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = arguments;
        var service = values is null ? null : ReadOptionalString(values.Value, "service");
        var method = values is null ? null : ReadOptionalString(values.Value, "method");
        var search = values is null ? null : ReadOptionalString(values.Value, "search");
        var offset = values is null ? 0 : ReadBoundedInteger(values.Value, "offset", 0, 0, int.MaxValue);
        var limit = values is null ? 25 : ReadBoundedInteger(values.Value, "limit", 25, 1, 100);

        var operations = ApiOperationCatalog.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(service))
        {
            var serviceKind = ParseServiceKind(service);
            operations = operations.Where(operation => operation.Service == serviceKind);
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            var normalizedMethod = ParseHttpMethod(method).Method;
            operations = operations.Where(operation => string.Equals(operation.Method, normalizedMethod, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (search.Length > 256)
            {
                throw new InvalidOperationException("Parameter 'search' must not exceed 256 characters.");
            }

            operations = operations.Where(operation =>
                operation.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                operation.OperationId?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                operation.Summary?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                operation.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var matching = operations
            .OrderBy(static operation => operation.Service)
            .ThenBy(static operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(static operation => operation.Method, StringComparer.Ordinal)
            .ToArray();
        var page = matching.Skip(offset).Take(limit).Select(static operation => new
        {
            service = operation.Service.ToString(),
            operation.Version,
            operation.Method,
            operation.Path,
            operation.OperationId,
            operation.Summary,
            operation.Tags,
            operation.HasRequestBody,
            operation.RequestBodyRequired,
            operation.Mutating
        }).ToArray();

        var payload = JsonSerializer.SerializeToElement(new
        {
            offset,
            limit,
            count = page.Length,
            totalCount = matching.Length,
            operations = page
        }, JsonOptions);
        return Task.FromResult(new McpCallToolResult(
            [new McpTextContent("text", $"Found {matching.Length} matching official UniFi API operations.")],
            payload));
    }

    private async Task<McpCallToolResult> ExecuteApiRequestAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = _clientFactory.Create(ReadRequiredString(values, "scope"));
        var method = ParseHttpMethod(ReadRequiredString(values, "method"));
        var path = ReadRequiredString(values, "path");
        var isMutation = method != HttpMethod.Get;
        var operation = ResolveOperation(client.Service, method.Method, StripQuery(path));
        ValidateConnectorRequest(client, operation, StripQuery(path));

        byte[]? body = null;
        if (values.TryGetProperty("body", out var bodyElement))
        {
            body = JsonSerializer.SerializeToUtf8Bytes(bodyElement, JsonOptions);
            if (body.Length > _options.MaxRequestBodyBytes)
            {
                throw new InvalidOperationException($"Request body exceeds the {_options.MaxRequestBodyBytes}-byte limit.");
            }
        }

        if (method == HttpMethod.Get && body is not null)
        {
            throw new InvalidOperationException("GET requests cannot include a request body.");
        }

        if (!operation.HasRequestBody && body is not null)
        {
            throw new InvalidOperationException($"Official operation {operation.Method} {operation.Path} does not accept a request body.");
        }

        if (operation.RequestBodyRequired && body is null)
        {
            throw new InvalidOperationException($"Official operation {operation.Method} {operation.Path} requires a request body.");
        }

        if (isMutation)
        {
            _mutationApprovalValidator.Validate(
                ReadRequiredString(values, "mutationApprovalToken"),
                client.ProfileName,
                method.Method,
                path,
                body);
        }

        var request = new UniFiApiRequest(
            method,
            path,
            body,
            body is null ? null : "application/json");
        var includeRaw = ReadOptionalBoolean(values, "includeRaw") && _options.AllowRawResponses;
        return await SendEndpointAsync(client, request, includeRaw, cancellationToken).ConfigureAwait(false);
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
        return await ReadEndpointAsync(client, path, includeRaw, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadSiteManagerCollectionAsync(
        JsonElement? arguments,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.SiteManager);
        var pageSize = ReadBoundedInteger(values, "pageSize", _options.MaxCollectionItems, 1, _options.MaxCollectionItems);
        var query = new List<KeyValuePair<string, string>>
        {
            new("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        AddOptionalQueryValue(values, "nextToken", query, maxLength: 4096);
        return await ReadEndpointAsync(client, BuildPath(endpoint, query), includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadSiteManagerDevicesAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.SiteManager);
        var pageSize = ReadBoundedInteger(values, "pageSize", _options.MaxCollectionItems, 1, _options.MaxCollectionItems);
        var query = new List<KeyValuePair<string, string>>
        {
            new("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        AddOptionalQueryValue(values, "nextToken", query, maxLength: 4096);
        AddOptionalQueryValue(values, "time", query, maxLength: 64);

        if (values.TryGetProperty("hostIds", out var hostIdsElement))
        {
            if (hostIdsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Parameter 'hostIds' must be an array.");
            }

            var hostIds = hostIdsElement.EnumerateArray().Take(25).Select(static item =>
                item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : throw new InvalidOperationException("Each hostIds item must be a string."));

            foreach (var hostId in hostIds)
            {
                if (string.IsNullOrWhiteSpace(hostId) || hostId.Length > 512)
                {
                    throw new InvalidOperationException("Each hostIds item must contain 1-512 characters.");
                }

                query.Add(new KeyValuePair<string, string>("hostIds[]", hostId));
            }
        }

        return await ReadEndpointAsync(client, BuildPath("/v1/devices", query), includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadNetworkInfoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.Network);
        return await ReadEndpointAsync(client, "/v1/info", includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadSiteManagerIspMetricsAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.SiteManager);
        var metricType = ReadRequiredString(values, "type");
        if (metricType is not ("5m" or "1h"))
        {
            throw new InvalidOperationException("Parameter 'type' must be '5m' or '1h'.");
        }

        var query = new List<KeyValuePair<string, string>>();
        var duration = ReadOptionalString(values, "duration");
        var beginTimestamp = ReadOptionalString(values, "beginTimestamp");
        var endTimestamp = ReadOptionalString(values, "endTimestamp");

        if (!string.IsNullOrWhiteSpace(duration))
        {
            if (duration is not ("24h" or "7d" or "30d"))
            {
                throw new InvalidOperationException("Parameter 'duration' must be '24h', '7d', or '30d'.");
            }

            if (beginTimestamp is not null || endTimestamp is not null)
            {
                throw new InvalidOperationException("Use either 'duration' or begin/end timestamps, not both.");
            }

            query.Add(new KeyValuePair<string, string>("duration", duration));
        }
        else
        {
            if (beginTimestamp is null || endTimestamp is null ||
                !DateTimeOffset.TryParse(beginTimestamp, out var begin) ||
                !DateTimeOffset.TryParse(endTimestamp, out var end) ||
                begin >= end)
            {
                throw new InvalidOperationException("Provide a duration or valid RFC3339 beginTimestamp earlier than endTimestamp.");
            }

            query.Add(new KeyValuePair<string, string>("beginTimestamp", beginTimestamp));
            query.Add(new KeyValuePair<string, string>("endTimestamp", endTimestamp));
        }

        return await ReadEndpointAsync(
            client,
            BuildPath($"/v1/isp-metrics/{metricType}", query),
            includeRaw: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadNetworkCollectionAsync(
        JsonElement? arguments,
        string endpointTemplate,
        bool requireSiteId,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.Network);
        var endpoint = endpointTemplate;

        if (requireSiteId)
        {
            endpoint = endpoint.Replace("{siteId}", ReadUuid(values, "siteId"), StringComparison.Ordinal);
        }

        var limit = ReadBoundedInteger(values, "limit", _options.MaxCollectionItems, 1, Math.Min(200, _options.MaxCollectionItems));
        var offset = ReadBoundedInteger(values, "offset", 0, 0, int.MaxValue);
        var query = new List<KeyValuePair<string, string>>
        {
            new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        AddOptionalQueryValue(values, "filter", query, maxLength: 1024);
        return await ReadEndpointAsync(client, BuildPath(endpoint, query), includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadNetworkDeviceStatisticsAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.Network);
        var siteId = ReadUuid(values, "siteId");
        var deviceId = ReadUuid(values, "deviceId");
        var path = $"/v1/sites/{siteId}/devices/{deviceId}/statistics/latest";
        return await ReadEndpointAsync(client, path, includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadServiceEndpointAsync(
        JsonElement? arguments,
        UniFiServiceKind service,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, service);
        return await ReadEndpointAsync(client, endpoint, includeRaw: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadMobilityDevicesAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        var values = RequireArguments(arguments);
        var client = GetServiceClient(values, UniFiServiceKind.Mobility);
        var workspaceId = ReadUuid(values, "workspaceId");
        var limit = ReadBoundedInteger(values, "limit", _options.MaxCollectionItems, 1, Math.Min(200, _options.MaxCollectionItems));
        var offset = ReadBoundedInteger(values, "offset", 0, 0, int.MaxValue);
        var query = new List<KeyValuePair<string, string>>
        {
            new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        return await ReadEndpointAsync(
            client,
            BuildPath($"/v1/mobility/workspaces/{workspaceId}/devices", query),
            includeRaw: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpCallToolResult> ReadEndpointAsync(
        IUniFiApiClient client,
        string path,
        bool includeRaw,
        CancellationToken cancellationToken) =>
        await SendEndpointAsync(
            client,
            UniFiApiRequest.Get(path),
            includeRaw,
            cancellationToken).ConfigureAwait(false);

    private async Task<McpCallToolResult> SendEndpointAsync(
        IUniFiApiClient client,
        UniFiApiRequest request,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await BoundedResponseReader.ReadAsStringAsync(
            response,
            _options.MaxUpstreamResponseBytes,
            client.ProfileName,
            request.RelativePath,
            cancellationToken).ConfigureAwait(false);

        var payload = new Dictionary<string, object?>
        {
            ["scope"] = client.ProfileName,
            ["method"] = request.Method.Method,
            ["path"] = StripQuery(request.RelativePath),
            ["statusCode"] = (int)response.StatusCode,
            ["data"] = JsonSanitizer.SanitizeToElement(
                responseBody,
                _options.MaxCollectionItems,
                _options.MaxObjectProperties,
                _options.MaxStringLength,
                _options.MaxSanitizedOutputCharacters,
                _options.MaxJsonDepth)
        };

        if (includeRaw)
        {
            payload["raw"] = JsonSanitizer.Summarize(
                responseBody,
                _options.MaxCollectionItems,
                _options.MaxObjectProperties,
                _options.MaxStringLength,
                _options.MaxSanitizedOutputCharacters,
                _options.MaxJsonDepth);
        }

        var action = request.Method == HttpMethod.Get ? "Read-only request" : $"{request.Method.Method} request";
        return new McpCallToolResult(
            [new McpTextContent("text", $"{action} completed for scope '{client.ProfileName}'.")],
            JsonSerializer.SerializeToElement(payload, JsonOptions));
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

    private IUniFiApiClient GetServiceClient(JsonElement arguments, UniFiServiceKind expectedService)
    {
        var scopeName = ReadRequiredString(arguments, "scope");
        var client = _clientFactory.Create(scopeName);
        if (client.Service != expectedService)
        {
            throw new InvalidOperationException(
                $"Scope '{scopeName}' is configured for {client.Service}, but this tool requires {expectedService}.");
        }

        return client;
    }

    private static JsonElement RequireArguments(JsonElement? arguments) =>
        arguments ?? throw new InvalidOperationException("arguments are required.");

    private static int ReadBoundedInteger(JsonElement arguments, string propertyName, int defaultValue, int minimum, int maximum)
    {
        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"Parameter '{propertyName}' must be an integer from {minimum} through {maximum}.");
        }

        return value;
    }

    private static string ReadUuid(JsonElement arguments, string propertyName)
    {
        var value = ReadRequiredString(arguments, propertyName);
        if (!Guid.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Parameter '{propertyName}' must be a UUID.");
        }

        return parsed.ToString("D");
    }

    private static UniFiServiceKind ParseServiceKind(string service) =>
        service switch
        {
            "siteManager" => UniFiServiceKind.SiteManager,
            "network" => UniFiServiceKind.Network,
            "protect" => UniFiServiceKind.Protect,
            "access" => UniFiServiceKind.Access,
            "mobility" => UniFiServiceKind.Mobility,
            _ => throw new InvalidOperationException(
                $"Parameter 'service' must be one of: {string.Join(", ", ServiceNames)}.")
        };

    private static HttpMethod ParseHttpMethod(string method) =>
        method.Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => throw new InvalidOperationException("HTTP method must be GET, POST, PUT, PATCH, or DELETE.")
        };

    private static ApiOperation ResolveOperation(UniFiServiceKind service, string method, string path)
    {
        var matches = ApiOperationCatalog.GetAll()
            .Where(operation =>
                operation.Service == service &&
                string.Equals(operation.Method, method, StringComparison.OrdinalIgnoreCase) &&
                MatchesOperationPath(operation.Path, path))
            .ToArray();

        if (matches.Length <= 1)
        {
            return matches.Length == 1
               ? matches[0]
               : throw new InvalidOperationException(
                   $"No official {service} API operation matches {method} {path}.");
        }

        var ranked = matches
            .Select(operation => new { Operation = operation, Score = GetOperationPathSpecificity(operation.Path) })
            .OrderByDescending(static candidate => candidate.Score)
            .ToArray();
        if (ranked[0].Score == ranked[1].Score)
        {
            throw new InvalidOperationException(
               $"Multiple official {service} API operations match {method} {path}.");
        }

        return ranked[0].Operation;
    }

    private static int GetOperationPathSpecificity(string template) =>
        template.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Sum(static segment =>
               segment.Equals("{*path}", StringComparison.Ordinal) ||
               segment.Equals("*path", StringComparison.Ordinal)
                   ? 0
                   : segment.StartsWith('{') && segment.EndsWith('}')
                       ? 1
                       : 100 + segment.Length);

    private static void ValidateConnectorRequest(IUniFiApiClient client, ApiOperation operation, string path)
    {
        if (!string.Equals(operation.OperationId, "ConnectorGet", StringComparison.Ordinal) &&
            !string.Equals(operation.OperationId, "ConnectorPost", StringComparison.Ordinal) &&
            !string.Equals(operation.OperationId, "ConnectorPut", StringComparison.Ordinal) &&
            !string.Equals(operation.OperationId, "ConnectorPatch", StringComparison.Ordinal) &&
            !string.Equals(operation.OperationId, "ConnectorDelete", StringComparison.Ordinal))
        {
            return;
        }

        if (!client.AllowConnectorProxy)
        {
            throw new InvalidOperationException($"Scope '{client.ProfileName}' does not enable Site Manager connector proxy.");
        }

        var marker = "/proxy/";
        var markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Connector path must contain an explicitly allowed proxy target.");
        }

        var connectorPath = path[markerIndex..];
        if (!client.ConnectorAllowedPathPrefixes.Any(prefix =>
                connectorPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                connectorPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Connector target '{connectorPath}' is outside the configured connector path scope.");
        }
    }

    private static bool MatchesOperationPath(string template, string path)
    {
        var pattern = string.Join(
            "/",
            template.Split('/').Select(static segment =>
                segment.Equals("{*path}", StringComparison.Ordinal) ||
                segment.Equals("*path", StringComparison.Ordinal)
                    ? ".+"
                    : segment.StartsWith('{') && segment.EndsWith('}')
                        ? "[^/]+"
                        : Regex.Escape(segment)));
        return Regex.IsMatch(path, $"^{pattern}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static void AddOptionalQueryValue(
        JsonElement arguments,
        string propertyName,
        ICollection<KeyValuePair<string, string>> query,
        int maxLength)
    {
        if (!arguments.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Parameter '{propertyName}' must be a string.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new InvalidOperationException($"Parameter '{propertyName}' must contain 1-{maxLength} characters.");
        }

        query.Add(new KeyValuePair<string, string>(propertyName, value));
    }

    private static string BuildPath(string endpoint, IEnumerable<KeyValuePair<string, string>> query)
    {
        var encoded = query.Select(static item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}");
        return $"{endpoint}?{string.Join("&", encoded)}";
    }

    private static string StripQuery(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? path : path[..queryIndex];
    }

    private static object BuildScopeOnlySchema() => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "Configured scope name." }
        },
        required = DescribeScopeRequiredProperties,
        additionalProperties = false
    };

    private static object BuildSiteManagerListSchema() => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "Configured Site Manager scope name." },
            pageSize = new { type = "integer", minimum = 1, maximum = 25, @default = 25 },
            nextToken = new { type = "string", description = "Opaque nextToken returned by the previous page." }
        },
        required = DescribeScopeRequiredProperties,
        additionalProperties = false
    };

    private static object BuildSiteManagerDeviceSchema() => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "Configured Site Manager scope name." },
            pageSize = new { type = "integer", minimum = 1, maximum = 25, @default = 25 },
            nextToken = new { type = "string" },
            hostIds = new { type = "array", maxItems = 25, items = new { type = "string" } },
            time = new { type = "string", format = "date-time" }
        },
        required = DescribeScopeRequiredProperties,
        additionalProperties = false
    };

    private static object BuildNetworkListSchema(bool requireSiteId) => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "Configured Network scope name." },
            siteId = new { type = "string", format = "uuid" },
            offset = new { type = "integer", minimum = 0, @default = 0 },
            limit = new { type = "integer", minimum = 1, maximum = 25, @default = 25 },
            filter = new { type = "string", description = "Optional Network API filter expression." }
        },
        required = requireSiteId ? SiteIdRequiredProperties : DescribeScopeRequiredProperties,
        additionalProperties = false
    };

    private static object BuildIspMetricsSchema() => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "Configured Site Manager scope name." },
            type = new { type = "string", @enum = new[] { "5m", "1h" } },
            duration = new { type = "string", @enum = new[] { "24h", "7d", "30d" } },
            beginTimestamp = new { type = "string", format = "date-time" },
            endTimestamp = new { type = "string", format = "date-time" }
        },
        required = new[] { "scope", "type" },
        additionalProperties = false
    };

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
