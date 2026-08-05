using System.Net;
using System.Text;
using System.Text.Json;
using Unifi.Mcp.Client;
using UnifiMcp.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("uses api key credentials without login", UsesApiKeyCredentialsWithoutLoginAsync),
    ("reuses cached token per profile", ReusesCachedTokenPerProfileAsync),
    ("reauthenticates once on unauthorized", ReauthenticatesOnUnauthorizedAsync),
    ("does not retry mutations on unauthorized", DoesNotRetryMutationsOnUnauthorizedAsync),
    ("rejects requests outside configured scope", RejectsRequestsOutsideScopeAsync),
    ("rejects ambiguous and traversal paths", RejectsAmbiguousPathsAsync),
    ("preserves controller base paths", PreservesControllerBasePathsAsync),
    ("rejects non-GET requests", RejectsNonGetRequestsAsync),
    ("allows configured mutation methods", AllowsConfiguredMutationMethodsAsync),
    ("keeps authentication errors secret-safe", KeepsAuthenticationErrorsSecretSafeAsync),
    ("loads profile config from JSON", LoadsProfileConfigFromJsonAsync),
    ("supports initialize and tools list", SupportsInitializeAndToolsListAsync),
    ("lists full official API catalog", ListsFullOfficialApiCatalogAsync),
    ("retrieves official operation schemas", RetrievesOfficialOperationSchemasAsync),
    ("rejects undocumented API operations", RejectsUndocumentedApiOperationsAsync),
    ("prefers literal API operation paths", PrefersLiteralApiOperationPathsAsync),
    ("requires explicit connector proxy enablement", RequiresExplicitConnectorProxyEnablementAsync),
    ("restricts connector proxy targets", RestrictsConnectorProxyTargetsAsync),
    ("requires mutation confirmation", RequiresMutationConfirmationAsync),
    ("enforces official request body requirements", EnforcesOfficialRequestBodyRequirementsAsync),
    ("executes confirmed mutation requests", ExecutesConfirmedMutationRequestsAsync),
    ("bounds mutation request bodies", BoundsMutationRequestBodiesAsync),
    ("ignores initialized notifications", IgnoresInitializedNotificationAsync),
    ("ignores request-method notifications", IgnoresRequestMethodNotificationsAsync),
    ("returns null IDs on JSON-RPC errors", ReturnsNullIdsOnJsonRpcErrorsAsync),
    ("distinguishes null IDs from notifications", DistinguishesNullIdsFromNotificationsAsync),
    ("rejects structurally invalid JSON-RPC", RejectsStructurallyInvalidJsonRpcAsync),
    ("returns tool failures as tool results", ReturnsToolFailuresAsToolResultsAsync),
    ("reads and writes newline stdio messages", ReadsAndWritesMessagesAsync),
    ("rejects oversized stdio messages", RejectsOversizedStdioMessagesAsync),
    ("continues after oversized stdio messages", ContinuesAfterOversizedStdioMessagesAsync),
    ("executes concrete Site Manager reads", ExecutesConcreteSiteManagerReadAsync),
    ("bounds numeric sanitizer output", BoundsNumericSanitizerOutputAsync),
    ("enforces concrete tool service type", EnforcesConcreteToolServiceTypeAsync),
    ("executes concrete Protect reads", ExecutesConcreteProtectReadAsync),
    ("executes concrete Access reads with bearer keys", ExecutesConcreteAccessReadAsync),
    ("executes concrete Mobility reads", ExecutesConcreteMobilityReadAsync),
    ("discovers added service operation schemas", DiscoversAddedServiceOperationSchemasAsync),
    ("rejects undocumented added service operations", RejectsUndocumentedAddedServiceOperationsAsync),
    ("requires approval for added service mutations", RequiresApprovalForAddedServiceMutationsAsync),
    ("rejects oversized upstream responses", RejectsOversizedUpstreamResponsesAsync),
    ("parses cookie headers defensively", ParsesCookieHeadersDefensivelyAsync),
    ("finds JSON values by name", FindsJsonValuesByNameAsync),
    ("validates access profile edge cases", ValidatesAccessProfileEdgeCasesAsync),
    ("validates client option edge cases", ValidatesClientOptionEdgeCasesAsync),
    ("rejects reserved caller headers", RejectsReservedCallerHeadersAsync),
    ("surfaces transport failures as retryable", SurfacesTransportFailuresAsRetryableAsync),
    ("validates server option edge cases", ValidatesServerOptionEdgeCasesAsync),
    ("validates configuration edge cases", ValidatesConfigurationEdgeCasesAsync),
    ("maps configuration to client profiles", MapsConfigurationToClientProfilesAsync),
    ("handles stdio CRLF partials and write validation", HandlesStdioCrLfPartialsAndWriteValidationAsync)
};

foreach (var (name, run) in tests)
{
    await run().ConfigureAwait(false);
    Console.WriteLine($"PASS {name}");
}

static async Task ReusesCachedTokenPerProfileAsync()
{
    var siteATransport = new ScriptedTransport(request =>
    {
        var path = RequestPathHelper.GetPath(request);
        return path switch
        {
            "/api/auth/login" => Responses.Login("SESSION_A=alpha"),
            "/proxy/network/api/s/site-a/stat/device" => Responses.Json(request, """{"count":1}"""),
            _ => throw new InvalidOperationException($"Unexpected request path '{path}'.")
        };
    });

    var siteBTransport = new ScriptedTransport(request =>
    {
        var path = RequestPathHelper.GetPath(request);
        return path switch
        {
            "/api/auth/login" => Responses.Login("SESSION_B=bravo"),
            "/proxy/network/api/s/site-b/stat/device" => Responses.Json(request, """{"count":2}"""),
            _ => throw new InvalidOperationException($"Unexpected request path '{path}'.")
        };
    });

    using var factory = CreateFactory(
        [
            CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat"),
            CreateProfile("site-b", "https://controller-b.example.invalid", "/proxy/network/api/s/site-b/stat")
        ],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = siteATransport,
            ["site-b"] = siteBTransport
        });

    using var siteAClient = factory.Create("site-a");
    using var siteBClient = factory.Create("site-b");

    using (var first = await siteAClient.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-a/stat/device")).ConfigureAwait(false))
    {
        _ = await first.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    using (var second = await siteAClient.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-a/stat/device")).ConfigureAwait(false))
    {
        _ = await second.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    using (var third = await siteBClient.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-b/stat/device")).ConfigureAwait(false))
    {
        _ = await third.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    Ensure(siteATransport.LoginRequestCount == 1, $"Expected one site-a login, got {siteATransport.LoginRequestCount}.");
    Ensure(siteBTransport.LoginRequestCount == 1, $"Expected one site-b login, got {siteBTransport.LoginRequestCount}.");
    Ensure(siteATransport.ApiRequestCount == 2, $"Expected two site-a API requests, got {siteATransport.ApiRequestCount}.");
    Ensure(siteBTransport.ApiRequestCount == 1, $"Expected one site-b API request, got {siteBTransport.ApiRequestCount}.");
    Ensure(siteATransport.CapturedCookies.All(cookie => cookie.Contains("SESSION_A=alpha", StringComparison.Ordinal)), "Expected cached site-a session cookie to be reused.");
}

static async Task UsesApiKeyCredentialsWithoutLoginAsync()
{
    const string ApiKey = "site-manager-key-value";

    var transport = new ScriptedTransport(request =>
    {
        if (RequestPathHelper.GetPath(request) == "/v1/hosts")
        {
            return Responses.Json(request, """{"items":[{"id":"host-1"}]}""");
        }

        throw new InvalidOperationException($"Unexpected request path '{RequestPathHelper.GetPath(request)}'.");
    });

    using var factory = CreateFactory(
        [CreateProfile("site-manager", "https://api.ui.com", "/v1", apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-manager"] = transport
        },
        environmentVariableReader: name => name == "UNIFI_SITE_MANAGER_API_KEY" ? ApiKey : null);

    using var client = factory.Create("site-manager");
    using var response = await client.SendAsync(UniFiApiRequest.Get("/v1/hosts")).ConfigureAwait(false);
    _ = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    Ensure(transport.LoginRequestCount == 0, $"Expected no login requests for API key auth, got {transport.LoginRequestCount}.");
    Ensure(transport.ApiKeyHeaders.Contains(ApiKey, StringComparer.Ordinal), "Expected the API key to be sent on the request.");
}

static async Task ReauthenticatesOnUnauthorizedAsync()
{
    var loginResponses = 0;
    var apiResponses = 0;

    var scriptedTransport = new ScriptedTransport(request =>
    {
        var path = RequestPathHelper.GetPath(request);
        if (path == "/api/auth/login")
        {
            loginResponses++;
            return loginResponses == 1
                ? Responses.Login("SESSION=first")
                : Responses.Login("SESSION=second");
        }

        if (path == "/proxy/network/api/s/site-a/stat/device")
        {
            apiResponses++;
            return apiResponses == 1
                ? Responses.Status(HttpStatusCode.Unauthorized, request)
                : Responses.Json(request, """{"ok":true}""");
        }

        throw new InvalidOperationException($"Unexpected request path '{path}'.");
    });

    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = scriptedTransport
        });

    using var client = factory.Create("site-a");
    using var response = await client.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-a/stat/device")).ConfigureAwait(false);
    _ = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    Ensure(scriptedTransport.LoginRequestCount == 2, $"Expected two logins after a 401, got {scriptedTransport.LoginRequestCount}.");
    Ensure(scriptedTransport.ApiRequestCount == 2, $"Expected request retry after a 401, got {scriptedTransport.ApiRequestCount}.");
    Ensure(scriptedTransport.CapturedCookies.First().Contains("SESSION=first", StringComparison.Ordinal), "Expected first API request to use the original session.");
    Ensure(scriptedTransport.CapturedCookies.Last().Contains("SESSION=second", StringComparison.Ordinal), "Expected retry to use the refreshed session.");
}

static async Task DoesNotRetryMutationsOnUnauthorizedAsync()
{
    var transport = new ScriptedTransport(request =>
        request.Method == HttpMethod.Post
            ? Responses.Status(HttpStatusCode.Unauthorized, request)
            : throw new InvalidOperationException("Unexpected request."));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            allowMutations: true,
            allowedHttpMethods: ["GET", "POST"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    using var client = factory.Create("network");

    await AssertThrowsAsync<UniFiClientException>(() =>
        client.SendAsync(UniFiApiRequest.FromJson(HttpMethod.Post, "/v1/sites/site/devices", new { macAddress = "test" })))
        .ConfigureAwait(false);
    Ensure(transport.ApiRequestCount == 1, "Mutations must not be retried automatically after an unauthorized response.");
}

static async Task RejectsRequestsOutsideScopeAsync()
{
    var transport = new ScriptedTransport(_ => Responses.Login("SESSION=alpha"));

    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = transport
        });

    using var client = factory.Create("site-a");

    var exception = await AssertThrowsAsync<UniFiClientException>(() =>
        client.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-b/stat/device"))).ConfigureAwait(false);

    Ensure(exception.Message.Contains("outside the configured scope", StringComparison.OrdinalIgnoreCase), "Expected a scope rejection message.");
    Ensure(transport.LoginRequestCount == 0, "Out-of-scope requests should be rejected before authentication.");
}

static async Task RejectsAmbiguousPathsAsync()
{
    var profile = CreateProfile("network", "https://unifi/proxy/network/integration", "/v1");
    var paths = new[]
    {
        "/v1/%2e%2e/admin",
        "/v1%2fadmin",
        "/v1%5cadmin",
        "/v1\\admin",
        "/v1;admin",
        "/v1/../admin"
    };

    foreach (var path in paths)
    {
        await AssertThrowsAsync<UniFiClientException>(() =>
        {
            _ = UniFiPathScopeGuard.EnsureAllowed(profile, path);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }
}

static Task PreservesControllerBasePathsAsync()
{
    var profile = CreateProfile("network", "https://unifi/proxy/network/integration", "/v1");
    var requestPath = UniFiPathScopeGuard.EnsureAllowed(profile, "/v1/info?limit=1");
    var resolved = new Uri(new Uri("https://unifi/proxy/network/integration/"), requestPath);

    Ensure(
        resolved.PathAndQuery == "/proxy/network/integration/v1/info?limit=1",
        $"Expected the controller base path to be preserved, got '{resolved.PathAndQuery}'.");
    return Task.CompletedTask;
}

static async Task RejectsNonGetRequestsAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile("network", "https://unifi/proxy/network/integration", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport });
    using var client = factory.Create("network");

    await AssertThrowsAsync<UniFiClientException>(() =>
        client.SendAsync(UniFiApiRequest.FromJson(HttpMethod.Post, "/v1/sites", new { name = "blocked" }))).ConfigureAwait(false);
    Ensure(transport.ApiRequestCount == 0, "Non-GET requests must be rejected before transport.");
}

static async Task AllowsConfiguredMutationMethodsAsync()
{
    var transport = new ScriptedTransport(request =>
    {
        Ensure(request.Method == HttpMethod.Post, "Expected POST to reach the transport.");
        return Responses.Json(request, """{"created":true}""");
    });
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            allowMutations: true,
            allowedHttpMethods: ["GET", "POST", "PUT", "PATCH", "DELETE"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    using var client = factory.Create("network");

    using var response = await client.SendAsync(
        UniFiApiRequest.FromJson(HttpMethod.Post, "/v1/sites/site/devices", new { macAddress = "redacted-test" }))
        .ConfigureAwait(false);
    Ensure(response.StatusCode == HttpStatusCode.OK, "Configured POST should succeed.");
}

static async Task KeepsAuthenticationErrorsSecretSafeAsync()
{
    const string SecretPassword = "TopSecret123!";
    const string SecretToken = "sensitive-token";

    var transport = new ScriptedTransport(request =>
        Responses.Status(
            HttpStatusCode.Forbidden,
            request,
            $"{{\"token\":\"{SecretToken}\",\"detail\":\"password {SecretPassword} rejected\"}}"));

    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat", password: SecretPassword)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = transport
        });

    using var client = factory.Create("site-a");
    var exception = await AssertThrowsAsync<UniFiAuthenticationException>(() =>
        client.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site-a/stat/device"))).ConfigureAwait(false);

    Ensure(!exception.Message.Contains(SecretPassword, StringComparison.Ordinal), "Authentication error leaked the configured password.");
    Ensure(!exception.Message.Contains(SecretToken, StringComparison.Ordinal), "Authentication error leaked a returned token.");
}

static Task LoadsProfileConfigFromJsonAsync()
{
    const string json = """
                        {
                          "tokenRefreshSkew": "00:01:00",
                          "profiles": [
                            {
                              "name": "site-a",
                              "baseAddress": "https://controller-a.example.invalid",
                              "apiKeyEnvironmentVariable": "UNIFI_SITE_MANAGER_API_KEY",
                              "allowedRelativePathPrefixes": [
                                "/proxy/network/api/s/site-a/stat"
                              ]
                            }
                          ]
                        }
                        """;

    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    var options = UniFiApiClientOptionsLoader.Load(stream, name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "from-env" : null);

    Ensure(options.Profiles.Count == 1, $"Expected one profile, got {options.Profiles.Count}.");
    Ensure(options.Profiles[0].Name == "site-a", "Expected the profile name to be loaded.");
    Ensure(options.TokenRefreshSkew == TimeSpan.FromMinutes(1), "Expected token refresh skew to be loaded.");
    return Task.CompletedTask;
}

static async Task SupportsInitializeAndToolsListAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(request => RequestPathHelper.GetPath(request) switch
            {
                "/api/auth/login" => Responses.Login("SESSION=alpha"),
                "/proxy/network/api/s/site-a/stat/device" => Responses.Json(request, """{"count":1}"""),
                _ => throw new InvalidOperationException($"Unexpected request path '{RequestPathHelper.GetPath(request)}'.")
            })
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));

    var initializeResponse = await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""").ConfigureAwait(false);
    Ensure(initializeResponse is not null, "Initialize should return a response.");

    using var initializeDocument = JsonDocument.Parse(initializeResponse!);
    var initializeResult = initializeDocument.RootElement.GetProperty("result");
    Ensure(initializeResult.GetProperty("protocolVersion").GetString() == "2024-11-05", "Expected initialize to echo the negotiated protocol version.");

    var toolsResponse = await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""").ConfigureAwait(false);
    Ensure(toolsResponse is not null, "tools/list should return a response.");

    using var toolsDocument = JsonDocument.Parse(toolsResponse!);
    var tools = toolsDocument.RootElement.GetProperty("result").GetProperty("tools");
    Ensure(tools.GetArrayLength() == 25, $"Expected twenty-five tools, got {tools.GetArrayLength()}.");
}

static async Task ListsFullOfficialApiCatalogAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":30,"method":"tools/call","params":{"name":"unifi.api.operations.list","arguments":{"limit":100}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var structured = document.RootElement.GetProperty("result").GetProperty("structuredContent");
    Ensure(structured.GetProperty("totalCount").GetInt32() == 275, "Expected all 275 official API operations.");
    Ensure(structured.GetProperty("operations").EnumerateArray().Any(operation => operation.GetProperty("mutating").GetBoolean()),
        "Expected mutation operations in the official API catalog.");
}

static async Task RetrievesOfficialOperationSchemasAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":34,"method":"tools/call","params":{"name":"unifi.api.operation.get","arguments":{"service":"network","operationId":"createNetwork"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var structured = document.RootElement.GetProperty("result").GetProperty("structuredContent");
    Ensure(structured.GetProperty("method").GetString() == "POST", "Expected createNetwork POST schema.");
    Ensure(structured.GetProperty("requestBody").ValueKind == JsonValueKind.Object, "Expected a request body schema.");
    Ensure(structured.GetProperty("referencedSchemas").EnumerateObject().Any(), "Expected referenced schema definitions.");
}

static async Task RejectsUndocumentedApiOperationsAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":35,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"network","method":"GET","path":"/v1/undocumented"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Undocumented API operations must be rejected.");
    Ensure(transport.ApiRequestCount == 0, "Undocumented operation must not reach transport.");
}

static async Task PrefersLiteralApiOperationPathsAsync()
{
    var transport = new ScriptedTransport(request =>
        Responses.Json(request, """{"orderedIds":[]}"""));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":40,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"network","method":"GET","path":"/v1/sites/site/acl-rules/ordering"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(!document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Literal operation paths must take precedence over parameterized paths.");
    Ensure(transport.ApiRequestCount == 1, "Resolved literal operation must reach transport once.");
}

static async Task RequiresExplicitConnectorProxyEnablementAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "site-manager",
            "https://api.ui.com",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY",
            service: UniFiServiceKind.SiteManager)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["site-manager"] = transport },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":37,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"site-manager","method":"GET","path":"/v1/connector/consoles/console/proxy/network/integration/v1/sites"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Connector proxy must require explicit scope enablement.");
    Ensure(transport.ApiRequestCount == 0, "Disabled connector request must not reach transport.");
}

static async Task RestrictsConnectorProxyTargetsAsync()
{
    var transport = new ScriptedTransport(request =>
        Responses.Json(request, """{"items":[]}"""));
    using var factory = CreateFactory(
        [CreateProfile(
            "site-manager",
            "https://api.ui.com",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY",
            service: UniFiServiceKind.SiteManager,
            allowConnectorProxy: true,
            connectorAllowedPathPrefixes: ["/proxy/network/integration/v1"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["site-manager"] = transport },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));

    var deniedResponse = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":38,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"site-manager","method":"GET","path":"/v1/connector/consoles/console/proxy/protect/integration/v1/cameras"}}}""")
        .ConfigureAwait(false);
    using (var deniedDocument = JsonDocument.Parse(deniedResponse!))
    {
        Ensure(deniedDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
            "Connector proxy must reject targets outside configured prefixes.");
    }

    var allowedResponse = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":39,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"site-manager","method":"GET","path":"/v1/connector/consoles/console/proxy/network/integration/v1/sites"}}}""")
        .ConfigureAwait(false);
    using var allowedDocument = JsonDocument.Parse(allowedResponse!);
    Ensure(!allowedDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Connector proxy must allow explicitly configured targets.");
    Ensure(transport.ApiRequestCount == 1, "Only the allowed connector request should reach transport.");
}

static async Task RequiresMutationConfirmationAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network,
            allowMutations: true,
            allowedHttpMethods: ["GET", "POST"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":31,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"network","method":"POST","path":"/v1/sites/site/devices","body":{"macAddress":"test"}}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Mutation requests without confirmation must fail.");
    Ensure(transport.ApiRequestCount == 0, "Unconfirmed mutation must not reach transport.");
}

static async Task EnforcesOfficialRequestBodyRequirementsAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network,
            allowMutations: true,
            allowedHttpMethods: ["GET", "POST"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":36,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"network","method":"POST","path":"/v1/sites/site/networks","mutationApprovalToken":"invalid"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var text = document.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
    Ensure(text?.Contains("requires a request body", StringComparison.Ordinal) == true,
        "Required request bodies must be enforced from the official contract.");
    Ensure(transport.ApiRequestCount == 0, "Body-invalid operation must not reach transport.");
}

static async Task ExecutesConfirmedMutationRequestsAsync()
{
    const string ApprovalKey = "test-mutation-approval-key";
    const string Path = "/v1/sites/site/firewall/policies/policy";
    const string BodyJson = """{"enabled":true}""";
    Environment.SetEnvironmentVariable("UNIFI_MCP_MUTATION_APPROVAL_KEY", ApprovalKey, EnvironmentVariableTarget.Process);
    var transport = new ScriptedTransport(request =>
    {
        Ensure(request.Method == HttpMethod.Patch, "Expected PATCH mutation.");
        Ensure(RequestPathHelper.GetPath(request) == "/v1/sites/site/firewall/policies/policy", "Unexpected mutation path.");
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Ensure(body.Contains("\"enabled\":true", StringComparison.Ordinal), "Expected JSON mutation body.");
        return Responses.Json(request, """{"id":"policy","enabled":true}""");
    });
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network,
            allowMutations: true,
            allowedHttpMethods: ["GET", "PATCH"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var token = CreateMutationApprovalToken(ApprovalKey, "network", "PATCH", Path, Encoding.UTF8.GetBytes(BodyJson));
    using var bodyDocument = JsonDocument.Parse(BodyJson);
    var requestJson = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = 32,
        method = "tools/call",
        @params = new
        {
            name = "unifi.api.request",
            arguments = new
            {
                scope = "network",
                method = "PATCH",
                path = Path,
                body = bodyDocument.RootElement,
                mutationApprovalToken = token
            }
        }
    });
    var response = await host.HandleJsonRpcAsync(requestJson)
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var result = document.RootElement.GetProperty("result");
    Ensure(
        !result.GetProperty("isError").GetBoolean(),
        $"Confirmed configured mutation should succeed: {result.GetProperty("content")[0].GetProperty("text").GetString()}");
    Ensure(result.GetProperty("structuredContent").GetProperty("method").GetString() == "PATCH",
        "Mutation result should identify its method.");

    var replayResponse = await host.HandleJsonRpcAsync(requestJson).ConfigureAwait(false);
    using var replayDocument = JsonDocument.Parse(replayResponse!);
    Ensure(replayDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Mutation approval tokens must be one-time use.");
    Ensure(transport.ApiRequestCount == 1, "Replayed mutation must not reach transport.");
    Environment.SetEnvironmentVariable("UNIFI_MCP_MUTATION_APPROVAL_KEY", null, EnvironmentVariableTarget.Process);
}

static async Task BoundsMutationRequestBodiesAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "network",
            "https://unifi/proxy/network/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY",
            service: UniFiServiceKind.Network,
            allowMutations: true,
            allowedHttpMethods: ["GET", "POST"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory, new UnifiMcpServerOptions { MaxRequestBodyBytes = 32 }));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":33,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"network","method":"POST","path":"/v1/sites/site/devices","body":{"value":"0123456789012345678901234567890123456789"},"mutationApprovalToken":"invalid"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Oversized mutation body must fail.");
    Ensure(transport.ApiRequestCount == 0, "Oversized mutation body must not reach transport.");
}

static async Task IgnoresInitializedNotificationAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => Responses.Login("SESSION=alpha"))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""").ConfigureAwait(false);
    Ensure(response is null, "Initialized notifications should not produce a response.");
}

static async Task IgnoresRequestMethodNotificationsAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    Ensure(await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"unsupported"}}""").ConfigureAwait(false) is null,
        "Initialize notifications must not produce responses.");
    Ensure(await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","method":"tools/list"}""").ConfigureAwait(false) is null,
        "tools/list notifications must not produce responses.");
    Ensure(await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","method":"tools/call","params":{"name":"unifi.scopes.list"}}""").ConfigureAwait(false) is null,
        "tools/call notifications must not produce responses.");
}

static async Task ReturnsNullIdsOnJsonRpcErrorsAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync("{").ConfigureAwait(false);
    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Null,
        "JSON-RPC parse errors must include a null id.");
}

static async Task DistinguishesNullIdsFromNotificationsAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var nullIdResponse = await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":null,"method":"ping"}""").ConfigureAwait(false);
    using var nullIdDocument = JsonDocument.Parse(nullIdResponse!);
    Ensure(nullIdDocument.RootElement.GetProperty("id").ValueKind == JsonValueKind.Null,
        "An explicit null id is a request and must receive a response.");

    var invalidIdResponse = await host.HandleJsonRpcAsync("""{"jsonrpc":"2.0","id":{},"method":"ping"}""").ConfigureAwait(false);
    using var invalidIdDocument = JsonDocument.Parse(invalidIdResponse!);
    Ensure(invalidIdDocument.RootElement.GetProperty("error").GetProperty("code").GetInt32() == -32600,
        "Object-valued JSON-RPC ids must be rejected as invalid requests.");
}

static async Task RejectsStructurallyInvalidJsonRpcAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));

    foreach (var payload in new[] { "null", """{"jsonrpc":"2.0","id":1}""", """{"jsonrpc":"1.0","method":"ping"}""" })
    {
        var response = await host.HandleJsonRpcAsync(payload).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response!);
        Ensure(document.RootElement.GetProperty("error").GetProperty("code").GetInt32() == -32600,
            "Structurally invalid JSON-RPC must return Invalid Request.");
        Ensure(document.RootElement.GetProperty("id").ValueKind == JsonValueKind.Null,
            "Structurally invalid JSON-RPC must return a null id.");
    }
}

static async Task ReturnsToolFailuresAsToolResultsAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/proxy/network/api/s/site-a/stat")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => Responses.Login("SESSION=alpha"))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"unifi.scopes.get","arguments":{"scope":"missing"}}}""")
        .ConfigureAwait(false);

    Ensure(response is not null, "Tool failures should still return a response payload.");

    using var document = JsonDocument.Parse(response!);
    var result = document.RootElement.GetProperty("result");
    Ensure(result.GetProperty("isError").GetBoolean(), "Expected tool failure to be returned as an MCP tool error.");
}

static async Task ReadsAndWritesMessagesAsync()
{
    await using var stream = new MemoryStream();
    const string Payload = """{"jsonrpc":"2.0","id":1}""";

    await McpJsonRpcHost.WriteMessageAsync(stream, Payload).ConfigureAwait(false);
    stream.Position = 0;

    var roundTripped = await McpJsonRpcHost.ReadMessageAsync(stream, 1024).ConfigureAwait(false);
    Ensure(roundTripped == Payload, "Expected newline-delimited stdio to round-trip unchanged.");
}

static async Task RejectsOversizedStdioMessagesAsync()
{
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12345\n"));
    var exception = await AssertThrowsAsync<InvalidOperationException>(() =>
        McpJsonRpcHost.ReadMessageAsync(stream, 4)).ConfigureAwait(false);
    Ensure(exception.Message.Contains("exceeds", StringComparison.Ordinal), "Expected a bounded stdio error.");
}

static async Task ContinuesAfterOversizedStdioMessagesAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });
    var server = new UnifiMcpServer(factory, new UnifiMcpServerOptions { MaxStdioMessageBytes = 64 });
    var host = new McpJsonRpcHost(server);
    var oversized = new string('x', 65);
    await using var input = new MemoryStream(Encoding.UTF8.GetBytes(
        oversized + "\n" + """{"jsonrpc":"2.0","id":7,"method":"ping"}""" + "\n"));
    await using var output = new MemoryStream();

    await host.HandleStdioAsync(input, output).ConfigureAwait(false);
    var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Ensure(lines.Length == 2, $"Expected an error and a ping response, got {lines.Length} messages.");
    using var pingDocument = JsonDocument.Parse(lines[1]);
    Ensure(pingDocument.RootElement.GetProperty("id").GetInt32() == 7, "Expected stdio processing to continue after an oversized message.");
}

static async Task ExecutesConcreteSiteManagerReadAsync()
{
    const string ApiKey = "test-api-key";
    var transport = new ScriptedTransport(request =>
    {
        var path = RequestPathHelper.GetPath(request);
        Ensure(request.Method == HttpMethod.Get, "Concrete tools must issue GET requests only.");
        Ensure(path == "/v1/sites?pageSize=2", $"Unexpected Site Manager path '{path}'.");
        return Responses.Json(request, """{"data":[{"siteId":"site-1","meta":{"name":"HQ","gatewayMac":"aa:bb","ipAddrs":["10.0.0.5"],"last_ip":"10.0.0.6","fixed_ip":"10.0.0.7","serialno":"ABC123"},"statistics":{"num_clients":3}}],"nextToken":"next"}""");
    });

    using var factory = CreateFactory(
        [CreateProfile(
            "site-manager",
            "https://api.ui.com",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY",
            service: UniFiServiceKind.SiteManager)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-manager"] = transport
        },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? ApiKey : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"unifi.site_manager.sites.list","arguments":{"scope":"site-manager","pageSize":2}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var structured = document.RootElement.GetProperty("result").GetProperty("structuredContent");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("meta").GetProperty("gatewayMac").GetString() == "[redacted]",
        "Expected nested MAC identifiers to be redacted.");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("meta").GetProperty("ipAddrs").GetString() == "[redacted]",
        "Expected UniFi IP address arrays to be redacted.");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("meta").GetProperty("last_ip").GetString() == "[redacted]",
        "Expected last known UniFi IP fields to be redacted.");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("meta").GetProperty("fixed_ip").GetString() == "[redacted]",
        "Expected fixed UniFi IP fields to be redacted.");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("meta").GetProperty("serialno").GetString() == "[redacted]",
        "Expected abbreviated UniFi serial fields to be redacted.");
}

static async Task EnforcesConcreteToolServiceTypeAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("generic", "https://controller.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"unifi.network.info.get","arguments":{"scope":"generic"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Expected a service mismatch tool error.");
}

static async Task BoundsNumericSanitizerOutputAsync()
{
    var payload = """{"number":""" + new string('9', 500) + "}";
    var transport = new ScriptedTransport(request => Responses.Json(request, payload));
    using var factory = CreateFactory(
        [CreateProfile(
            "site-manager",
            "https://api.ui.com",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY",
            service: UniFiServiceKind.SiteManager)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["site-manager"] = transport },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "test-api-key" : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(
        factory,
        new UnifiMcpServerOptions { MaxSanitizedOutputCharacters = 64 }));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"unifi.site_manager.hosts.list","arguments":{"scope":"site-manager"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var sanitizedData = document.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("data");
    Ensure(sanitizedData.GetRawText().Length <= 64, "Numeric values must not bypass the aggregate sanitizer output limit.");
}

static async Task RejectsOversizedUpstreamResponsesAsync()
{
    var transport = new ScriptedTransport(request => Responses.Json(request, """{"data":"0123456789"}"""));
    using var factory = CreateFactory(
        [CreateProfile(
            "site-manager",
            "https://api.ui.com",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY",
            service: UniFiServiceKind.SiteManager)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-manager"] = transport
        },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "test-api-key" : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(
        factory,
        new UnifiMcpServerOptions { MaxUpstreamResponseBytes = 8 }));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"unifi.site_manager.hosts.list","arguments":{"scope":"site-manager"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Expected an oversized upstream response tool error.");
}

static async Task ExecutesConcreteProtectReadAsync()
{
    var transport = new ScriptedTransport(request =>
    {
        Ensure(request.Method == HttpMethod.Get, "Concrete Protect tools must issue GET requests only.");
        Ensure(RequestPathHelper.GetPath(request) == "/v1/cameras", "Unexpected Protect path.");
        return Responses.Json(request, """{"data":[{"id":"cam-1","name":"Front","mac":"aa:bb:cc:dd:ee:ff"}]}""");
    });

    using var factory = CreateFactory(
        [CreateProfile(
            "protect",
            "https://console.example.invalid/proxy/protect/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_PROTECT_API_KEY",
            service: UniFiServiceKind.Protect)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["protect"] = transport },
        name => name == "UNIFI_PROTECT_API_KEY" ? "protect-key" : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":60,"method":"tools/call","params":{"name":"unifi.protect.cameras.list","arguments":{"scope":"protect"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    var structured = document.RootElement.GetProperty("result").GetProperty("structuredContent");
    Ensure(structured.GetProperty("data").GetProperty("data")[0].GetProperty("mac").GetString() == "[redacted]",
        "Expected Protect MAC identifiers to be redacted.");
    Ensure(transport.ApiKeyHeaders.Contains("protect-key", StringComparer.Ordinal),
        "Expected the Protect API key header to be sent.");
}

static async Task ExecutesConcreteAccessReadAsync()
{
    var transport = new ScriptedTransport(request =>
    {
        Ensure(RequestPathHelper.GetPath(request) == "/api/v1/developer/doors", "Unexpected Access path.");
        return Responses.Json(request, """{"code":"SUCCESS","data":[{"id":"door-1","name":"Lobby"}]}""");
    });

    using var factory = CreateFactory(
        [CreateProfile(
            "access",
            "https://console.example.invalid:12445",
            "/api/v1/developer",
            apiKeyEnvironmentVariable: "UNIFI_ACCESS_API_TOKEN",
            apiKeyHeaderName: "Authorization",
            apiKeyValuePrefix: "Bearer",
            service: UniFiServiceKind.Access)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["access"] = transport },
        name => name == "UNIFI_ACCESS_API_TOKEN" ? "access-token" : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":61,"method":"tools/call","params":{"name":"unifi.access.doors.list","arguments":{"scope":"access"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(!document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Access reads must succeed.");
    Ensure(transport.AuthorizationHeaders.Contains("Bearer " + "access-token", StringComparer.Ordinal),
        "Expected the Access bearer token header to be sent.");
}

static async Task ExecutesConcreteMobilityReadAsync()
{
    const string WorkspaceId = "8b1f0d02-95a0-4a35-9a52-7f0c6c2d1f11";
    var transport = new ScriptedTransport(request =>
    {
        Ensure(
            RequestPathHelper.GetPath(request) == $"/v1/mobility/workspaces/{WorkspaceId}/devices?limit=5&offset=0",
            $"Unexpected Mobility path '{RequestPathHelper.GetPath(request)}'.");
        return Responses.Json(request, """{"data":[{"id":"device-1"}],"total":1}""");
    });

    using var factory = CreateFactory(
        [CreateProfile(
            "mobility",
            "https://api.ui.com",
            "/v1/mobility",
            apiKeyEnvironmentVariable: "UNIFI_MOBILITY_API_KEY",
            service: UniFiServiceKind.Mobility)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["mobility"] = transport },
        name => name == "UNIFI_MOBILITY_API_KEY" ? "mobility-key" : null);

    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        "{\"jsonrpc\":\"2.0\",\"id\":62,\"method\":\"tools/call\",\"params\":{\"name\":\"unifi.mobility.devices.list\",\"arguments\":{\"scope\":\"mobility\",\"workspaceId\":\"" + WorkspaceId + "\",\"limit\":5}}}")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(!document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Mobility reads must succeed.");
}

static async Task DiscoversAddedServiceOperationSchemasAsync()
{
    using var factory = CreateFactory(
        [CreateProfile("site-a", "https://controller-a.example.invalid", "/v1")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-a"] = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."))
        });
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));

    foreach (var (service, operationId, expectedMethod) in new[]
             {
                 ("protect", "patchCamerasById", "PATCH"),
                 ("access", "fetchAllDoors", "GET"),
                 ("mobility", "listWorkspaces", "GET")
             })
    {
        var response = await host.HandleJsonRpcAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":63,\"method\":\"tools/call\",\"params\":{\"name\":\"unifi.api.operation.get\",\"arguments\":{\"service\":\"" + service + "\",\"operationId\":\"" + operationId + "\"}}}")
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(response!);
        var structured = document.RootElement.GetProperty("result").GetProperty("structuredContent");
        Ensure(structured.GetProperty("method").GetString() == expectedMethod,
            $"Expected {operationId} to resolve to {expectedMethod}.");
    }

    var listResponse = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":64,"method":"tools/call","params":{"name":"unifi.api.operations.list","arguments":{"service":"protect","limit":1}}}""")
        .ConfigureAwait(false);
    using var listDocument = JsonDocument.Parse(listResponse!);
    Ensure(listDocument.RootElement.GetProperty("result").GetProperty("structuredContent")
        .GetProperty("totalCount").GetInt32() > 0, "Expected Protect operations to be discoverable by service.");
}

static async Task RejectsUndocumentedAddedServiceOperationsAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "protect",
            "https://console.example.invalid/proxy/protect/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_PROTECT_API_KEY",
            service: UniFiServiceKind.Protect)],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["protect"] = transport },
        name => name == "UNIFI_PROTECT_API_KEY" ? "protect-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":65,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"protect","method":"GET","path":"/v1/undocumented"}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Undocumented Protect operations must be rejected.");
    Ensure(transport.ApiRequestCount == 0, "Undocumented Protect operation must not reach transport.");
}

static async Task RequiresApprovalForAddedServiceMutationsAsync()
{
    var transport = new ScriptedTransport(_ => throw new InvalidOperationException("Transport should not be called."));
    using var factory = CreateFactory(
        [CreateProfile(
            "protect",
            "https://console.example.invalid/proxy/protect/integration",
            "/v1",
            apiKeyEnvironmentVariable: "UNIFI_PROTECT_API_KEY",
            service: UniFiServiceKind.Protect,
            allowMutations: true,
            allowedHttpMethods: ["GET", "PATCH"])],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["protect"] = transport },
        name => name == "UNIFI_PROTECT_API_KEY" ? "protect-key" : null);
    var host = new McpJsonRpcHost(new UnifiMcpServer(factory));
    var response = await host.HandleJsonRpcAsync(
        """{"jsonrpc":"2.0","id":66,"method":"tools/call","params":{"name":"unifi.api.request","arguments":{"scope":"protect","method":"PATCH","path":"/v1/cameras/cam-1","body":{"name":"Front"}}}}""")
        .ConfigureAwait(false);

    using var document = JsonDocument.Parse(response!);
    Ensure(document.RootElement.GetProperty("result").GetProperty("isError").GetBoolean(),
        "Protect mutations without approval must fail.");
    Ensure(transport.ApiRequestCount == 0, "Unapproved Protect mutation must not reach transport.");
}

static Task ParsesCookieHeadersDefensivelyAsync()
{
    using var response = new HttpResponseMessage(HttpStatusCode.OK);
    response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSION=first; Path=/; HttpOnly");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "  ");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "malformed");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "=missing-name");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "empty=   ; Path=/");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSION=second; Secure");
    response.Headers.TryAddWithoutValidation("Set-Cookie", "csrf=token=value; Path=/");

    var cookies = SetCookieParser.Parse(response);

    Ensure(cookies.Count == 2, $"Expected two parsed cookies, got {cookies.Count}.");
    Ensure(cookies["SESSION"] == "second", "Expected later duplicate cookies to win.");
    Ensure(cookies["csrf"] == "token=value", "Expected cookie values containing '=' to be preserved.");
    return Task.CompletedTask;
}

static Task FindsJsonValuesByNameAsync()
{
    using var document = JsonDocument.Parse(
        """{"items":[{"nested":{"count":"42","enabled":true}},{"id":123}],"ignored":null}""");

    Ensure(JsonSearch.TryFindString(document.RootElement, ["id"], out var id) && id == "123",
        "Expected numeric JSON values to be returned as strings.");
    Ensure(JsonSearch.TryFindString(document.RootElement, ["enabled"], out var enabled) && enabled == bool.TrueString,
        "Expected boolean JSON values to be returned as strings.");
    Ensure(JsonSearch.TryFindInt32(document.RootElement, ["count"], out var count) && count == 42,
        "Expected numeric strings to be parsed as Int32.");
    Ensure(!JsonSearch.TryFindString(document.RootElement, ["ignored"], out _),
        "Null JSON values should not be returned as strings.");
    Ensure(!JsonSearch.TryFindInt32(null, ["count"], out _),
        "Null JSON roots should not match values.");
    return Task.CompletedTask;
}

static Task ValidatesAccessProfileEdgeCasesAsync()
{
    var invalidProfiles = new (string Name, UniFiAccessProfileOptions Profile)[]
    {
        ("missing credential mode", new UniFiAccessProfileOptions { Name = "missing-credentials", BaseAddress = new Uri("https://unifi.example.invalid"), AllowedRelativePathPrefixes = ["/v1"] }),
        ("api key and password", new UniFiAccessProfileOptions { Name = "mixed-credentials", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", ApiKeyEnvironmentVariable = "UNIFI_KEY", AllowedRelativePathPrefixes = ["/v1"] }),
        ("non-https", CreateProfile("plain-http", "http://unifi.example.invalid", "/v1")),
        ("embedded credentials", new UniFiAccessProfileOptions { Name = "userinfo", BaseAddress = new UriBuilder(Uri.UriSchemeHttps, "unifi.example.invalid") { UserName = "user", Password = "password" }.Uri, Username = "user", Password = "password", AllowedRelativePathPrefixes = ["/v1"] }),
        ("bad pin", new UniFiAccessProfileOptions { Name = "bad-pin", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", PinnedServerCertificateSha256 = "not-a-pin", AllowedRelativePathPrefixes = ["/v1"] }),
        ("blank api key header", new UniFiAccessProfileOptions { Name = "blank-api-header", BaseAddress = new Uri("https://unifi.example.invalid"), ApiKeyEnvironmentVariable = "UNIFI_KEY", ApiKeyHeaderName = " ", AllowedRelativePathPrefixes = ["/v1"] }),
        ("bad api key prefix", new UniFiAccessProfileOptions { Name = "bad-api-prefix", BaseAddress = new Uri("https://unifi.example.invalid"), ApiKeyEnvironmentVariable = "UNIFI_KEY", ApiKeyValuePrefix = "Bearer!", AllowedRelativePathPrefixes = ["/v1"] }),
        ("missing login path", new UniFiAccessProfileOptions { Name = "missing-login", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", LoginPath = " ", AllowedRelativePathPrefixes = ["/v1"] }),
        ("missing path prefix", new UniFiAccessProfileOptions { Name = "missing-prefix", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", AllowedRelativePathPrefixes = [] }),
        ("unsupported method", new UniFiAccessProfileOptions { Name = "trace", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", AllowedRelativePathPrefixes = ["/v1"], AllowedHttpMethods = ["GET", "TRACE"] }),
        ("missing get", CreateProfile("missing-get", "https://unifi.example.invalid", "/v1", allowMutations: true, allowedHttpMethods: ["POST"])),
        ("mutation method without mutations", new UniFiAccessProfileOptions { Name = "post-without-mutations", BaseAddress = new Uri("https://unifi.example.invalid"), Username = "user", Password = "password", AllowedRelativePathPrefixes = ["/v1"], AllowedHttpMethods = ["GET", "POST"] }),
        ("connector wrong service", CreateProfile("connector-wrong-service", "https://unifi.example.invalid", "/v1", allowConnectorProxy: true, connectorAllowedPathPrefixes: ["/proxy"])),
        ("connector missing prefixes", new UniFiAccessProfileOptions { Name = "connector-missing-prefix", BaseAddress = new Uri("https://api.ui.com"), ApiKeyEnvironmentVariable = "UNIFI_KEY", Service = UniFiServiceKind.SiteManager, AllowedRelativePathPrefixes = ["/v1"], AllowConnectorProxy = true })
    };

    foreach (var (name, profile) in invalidProfiles)
    {
        _ = AssertThrows<Exception>(() => profile.Validate(variable => variable == "UNIFI_KEY" ? "key" : null), name);
    }

    var validPin = string.Concat(Enumerable.Repeat("a1", 32));
    var normalized = new UniFiAccessProfileOptions
    {
        Name = "normalizes",
        BaseAddress = new Uri("https://unifi.example.invalid"),
        Username = "user",
        Password = "password",
        AllowedRelativePathPrefixes = ["v1"],
        AllowMutations = true,
        AllowedHttpMethods = [" get ", "POST", "post"],
        PinnedServerCertificateSha256 = string.Join(':', Enumerable.Repeat("A1", 32))
    };
    normalized.Validate();
    Ensure(normalized.GetNormalizedAllowedPathPrefixes().Single() == "/v1", "Expected path prefixes to be normalized.");
    Ensure(normalized.GetNormalizedAllowedHttpMethods().SetEquals(["GET", "POST"]), "Expected HTTP methods to be normalized and deduplicated.");
    Ensure(validPin.Length == 64, "Expected valid certificate pin test data.");
    return Task.CompletedTask;
}

static Task ValidatesClientOptionEdgeCasesAsync()
{
    _ = AssertThrows<InvalidOperationException>(() => new UniFiApiClientOptions().Validate(), "missing profiles");
    _ = AssertThrows<InvalidOperationException>(() => new UniFiApiClientOptions
    {
        TokenRefreshSkew = TimeSpan.FromSeconds(-1),
        Profiles = [CreateProfile("network", "https://unifi.example.invalid", "/v1")]
    }.Validate(), "negative skew");
    _ = AssertThrows<InvalidOperationException>(() => new UniFiApiClientOptions
    {
        Profiles =
        [
            CreateProfile("network", "https://unifi.example.invalid", "/v1"),
            CreateProfile("NETWORK", "https://unifi.example.invalid", "/v1")
        ]
    }.Validate(), "duplicate profile names");
    return Task.CompletedTask;
}

static async Task RejectsReservedCallerHeadersAsync()
{
    var transport = new ScriptedTransport(_ => Responses.Json(new HttpRequestMessage(HttpMethod.Get, "/v1/hosts"), """{"items":[]}"""));
    using var factory = CreateFactory(
        [CreateProfile("site-manager", "https://api.ui.com", "/v1", apiKeyEnvironmentVariable: "UNIFI_SITE_MANAGER_API_KEY")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["site-manager"] = transport },
        name => name == "UNIFI_SITE_MANAGER_API_KEY" ? "test-api-key" : null);

    using var client = factory.Create("site-manager");
    var exception = await AssertThrowsAsync<InvalidOperationException>(() =>
        client.SendAsync(UniFiApiRequest.Get("/v1/hosts", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "******"
        }))).ConfigureAwait(false);

    Ensure(exception.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase), "Expected reserved header rejection.");
    Ensure(transport.ApiRequestCount == 0, "Reserved headers must be rejected before transport calls.");
}

static async Task SurfacesTransportFailuresAsRetryableAsync()
{
    var transport = new ScriptedTransport(_ => throw new HttpRequestException("network unavailable"));
    using var factory = CreateFactory(
        [CreateProfile("network", "https://controller.example.invalid", "/proxy/network/api/s/site/stat", apiKeyEnvironmentVariable: "UNIFI_NETWORK_API_KEY")],
        new Dictionary<string, ScriptedTransport>(StringComparer.OrdinalIgnoreCase) { ["network"] = transport },
        name => name == "UNIFI_NETWORK_API_KEY" ? "test-api-key" : null);

    using var client = factory.Create("network");
    var exception = await AssertThrowsAsync<UniFiClientException>(() =>
        client.SendAsync(UniFiApiRequest.Get("/proxy/network/api/s/site/stat/device?mac=redacted"))).ConfigureAwait(false);

    Ensure(exception.Retryable, "Transport failures before a response should be retryable.");
    Ensure(exception.ProfileName == "network", "Expected retryable transport failures to preserve the profile name.");
    Ensure(exception.InnerException is HttpRequestException, "Expected original transport exception to be preserved.");
}

static Task ValidatesServerOptionEdgeCasesAsync()
{
    var invalidOptions = new (string Name, UnifiMcpServerOptions Options)[]
    {
        ("blank name", new UnifiMcpServerOptions { Name = " " }),
        ("blank version", new UnifiMcpServerOptions { Version = " " }),
        ("blank protocol", new UnifiMcpServerOptions { ProtocolVersion = " " }),
        ("collection size", new UnifiMcpServerOptions { MaxCollectionItems = 0 }),
        ("object size", new UnifiMcpServerOptions { MaxObjectProperties = 0 }),
        ("string size", new UnifiMcpServerOptions { MaxStringLength = 0 }),
        ("upstream size", new UnifiMcpServerOptions { MaxUpstreamResponseBytes = 0 }),
        ("stdio size", new UnifiMcpServerOptions { MaxStdioMessageBytes = 0 }),
        ("sanitized size", new UnifiMcpServerOptions { MaxSanitizedOutputCharacters = 63 }),
        ("json depth", new UnifiMcpServerOptions { MaxJsonDepth = 0 }),
        ("request body size", new UnifiMcpServerOptions { MaxRequestBodyBytes = 0 }),
        ("approval env", new UnifiMcpServerOptions { MutationApprovalKeyEnvironmentVariable = " " }),
        ("approval age low", new UnifiMcpServerOptions { MutationApprovalMaxAgeSeconds = 29 }),
        ("approval age high", new UnifiMcpServerOptions { MutationApprovalMaxAgeSeconds = 3601 }),
        ("schema size", new UnifiMcpServerOptions { MaxOperationSchemaCharacters = 1023 })
    };

    foreach (var (name, options) in invalidOptions)
    {
        _ = AssertThrows<InvalidOperationException>(options.Validate, name);
    }

    new UnifiMcpServerOptions().Validate();
    return Task.CompletedTask;
}

static Task ValidatesConfigurationEdgeCasesAsync()
{
    _ = AssertThrows<InvalidOperationException>(() => new UnifiMcpConfiguration().Validate(), "missing credentials");
    _ = AssertThrows<InvalidOperationException>(() => new UnifiMcpConfiguration
    {
        TokenRefreshSkew = TimeSpan.FromSeconds(-1),
        Credentials = [new UniFiCredentialOptions { Name = "api", ApiKeyEnvironmentVariable = "UNIFI_KEY" }],
        Scopes = [new UniFiScopeOptions { Name = "scope", Credential = "api", BaseAddress = new Uri("https://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] }]
    }.Validate(name => name == "UNIFI_KEY" ? "key" : null), "negative skew");
    _ = AssertThrows<InvalidOperationException>(() => new UnifiMcpConfiguration
    {
        Credentials =
        [
            new UniFiCredentialOptions { Name = "api", ApiKeyEnvironmentVariable = "UNIFI_KEY" },
            new UniFiCredentialOptions { Name = "API", ApiKeyEnvironmentVariable = "UNIFI_KEY" }
        ],
        Scopes = [new UniFiScopeOptions { Name = "scope", Credential = "api", BaseAddress = new Uri("https://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] }]
    }.Validate(name => name == "UNIFI_KEY" ? "key" : null), "duplicate credentials");
    _ = AssertThrows<InvalidOperationException>(() => new UnifiMcpConfiguration
    {
        Credentials = [new UniFiCredentialOptions { Name = "api", ApiKeyEnvironmentVariable = "UNIFI_KEY" }],
        Scopes =
        [
            new UniFiScopeOptions { Name = "scope", Credential = "api", BaseAddress = new Uri("https://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] },
            new UniFiScopeOptions { Name = "SCOPE", Credential = "api", BaseAddress = new Uri("https://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] }
        ]
    }.Validate(name => name == "UNIFI_KEY" ? "key" : null), "duplicate scopes");
    _ = AssertThrows<InvalidOperationException>(() => new UnifiMcpConfiguration
    {
        Credentials = [new UniFiCredentialOptions { Name = "api", ApiKeyEnvironmentVariable = "UNIFI_KEY" }],
        Scopes = [new UniFiScopeOptions { Name = "scope", Credential = "missing", BaseAddress = new Uri("https://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] }]
    }.Validate(name => name == "UNIFI_KEY" ? "key" : null), "unknown credential");
    _ = AssertThrows<InvalidOperationException>(() => new UniFiCredentialOptions { Name = "mixed", ApiKeyEnvironmentVariable = "UNIFI_KEY", Username = "user", Password = "pass" }.Validate(name => name == "UNIFI_KEY" ? "key" : null), "mixed credential mode");
    _ = AssertThrows<InvalidOperationException>(() => new UniFiCredentialOptions { Name = "missing", Username = "user" }.Validate(), "partial username password");
    _ = AssertThrows<InvalidOperationException>(() => new UniFiScopeOptions { Name = "plain", Credential = "api", BaseAddress = new Uri("http://api.ui.com"), AllowedRelativePathPrefixes = ["/v1"] }.Validate(), "scope non-https");
    return Task.CompletedTask;
}

static Task MapsConfigurationToClientProfilesAsync()
{
    var options = ValidConfiguration().ToClientOptions(name => name == "UNIFI_KEY" ? "key" : null);
    var profile = options.Profiles.Single();

    Ensure(profile.Name == "scope", "Expected scope name to map to profile name.");
    Ensure(profile.ApiKeyEnvironmentVariable == "UNIFI_KEY", "Expected API key credential to map to profile.");
    Ensure(profile.ApiKeyHeaderName == "Authorization", "Expected API key header to map to profile.");
    Ensure(profile.ApiKeyValuePrefix == "Bearer", "Expected API key value prefix to map to profile.");
    Ensure(profile.Service == UniFiServiceKind.SiteManager, "Expected scope service to map to profile.");
    Ensure(profile.AllowConnectorProxy, "Expected connector proxy setting to map to profile.");
    Ensure(profile.GetNormalizedConnectorAllowedPathPrefixes().Single() == "/proxy/network", "Expected connector prefixes to map to profile.");
    return Task.CompletedTask;
}

static async Task HandlesStdioCrLfPartialsAndWriteValidationAsync()
{
    using var crlfInput = new MemoryStream(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\r\n"));
    var crlfMessage = await McpJsonRpcHost.ReadMessageAsync(crlfInput, 1024).ConfigureAwait(false);
    Ensure(crlfMessage == """{"jsonrpc":"2.0","method":"notifications/initialized"}""", "Expected CRLF line endings to be trimmed.");

    using var partialInput = new MemoryStream(Encoding.UTF8.GetBytes("""{"partial":true}"""));
    var partialMessage = await McpJsonRpcHost.ReadMessageAsync(partialInput, 1024).ConfigureAwait(false);
    Ensure(partialMessage == """{"partial":true}""", "Expected EOF-terminated partial messages to be returned.");

    using var emptyInput = new MemoryStream();
    var emptyMessage = await McpJsonRpcHost.ReadMessageAsync(emptyInput, 1024).ConfigureAwait(false);
    Ensure(emptyMessage is null, "Expected empty EOF to return null.");

    await AssertThrowsAsync<InvalidOperationException>(() =>
        McpJsonRpcHost.WriteMessageAsync(new MemoryStream(), "line1\nline2")).ConfigureAwait(false);
    await AssertThrowsAsync<ArgumentOutOfRangeException>(() =>
        McpJsonRpcHost.ReadMessageAsync(new MemoryStream(), 0)).ConfigureAwait(false);
}

static UniFiApiClientFactory CreateFactory(
    IReadOnlyList<UniFiAccessProfileOptions> profiles,
    IReadOnlyDictionary<string, ScriptedTransport> transports,
    Func<string, string?>? environmentVariableReader = null)
{
    return new UniFiApiClientFactory(
        new UniFiApiClientOptions
        {
            Profiles = profiles
        },
        environmentVariableReader: environmentVariableReader,
        transportFactory: new ScriptedTransportFactory(transports));
}

static UniFiAccessProfileOptions CreateProfile(
    string name,
    string baseAddress,
    string allowedPrefix,
    string password = "password",
    string? apiKeyEnvironmentVariable = null,
    string apiKeyHeaderName = "X-API-KEY",
    string? apiKeyValuePrefix = null,
    UniFiServiceKind service = UniFiServiceKind.Generic,
    bool allowMutations = false,
    IReadOnlyList<string>? allowedHttpMethods = null,
    bool allowConnectorProxy = false,
    IReadOnlyList<string>? connectorAllowedPathPrefixes = null)
{
    return new UniFiAccessProfileOptions
    {
        Name = name,
        BaseAddress = new Uri(baseAddress),
        Service = service,
        Username = apiKeyEnvironmentVariable is null ? "readonly" : null,
        Password = apiKeyEnvironmentVariable is null ? password : null,
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
        ApiKeyHeaderName = apiKeyHeaderName,
        ApiKeyValuePrefix = apiKeyValuePrefix,
        AllowedRelativePathPrefixes = [allowedPrefix],
        AllowMutations = allowMutations,
        AllowedHttpMethods = allowedHttpMethods ?? ["GET"],
        AllowConnectorProxy = allowConnectorProxy,
        ConnectorAllowedPathPrefixes = connectorAllowedPathPrefixes ?? []
    };
}

static UnifiMcpConfiguration ValidConfiguration()
{
    return new UnifiMcpConfiguration
    {
        Credentials =
        [
            new UniFiCredentialOptions
            {
                Name = "api",
                ApiKeyEnvironmentVariable = "UNIFI_KEY",
                ApiKeyHeaderName = "Authorization",
                ApiKeyValuePrefix = "Bearer"
            }
        ],
        Scopes =
        [
            new UniFiScopeOptions
            {
                Name = "scope",
                Credential = "api",
                BaseAddress = new Uri("https://api.ui.com"),
                Service = UniFiServiceKind.SiteManager,
                AllowedRelativePathPrefixes = ["/v1"],
                AllowConnectorProxy = true,
                ConnectorAllowedPathPrefixes = ["/proxy/network"]
            }
        ]
    };
}

static TException AssertThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {description} to throw {typeof(TException).Name}.");
}

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action().ConfigureAwait(false);
    }

    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
}

static string CreateMutationApprovalToken(string key, string scope, string method, string path, byte[] body)
    => MutationApprovalToken.Create(key, scope, method, path, body, DateTimeOffset.UtcNow.AddMinutes(2));

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class ScriptedTransportFactory : IUniFiTransportFactory
{
    private readonly IReadOnlyDictionary<string, ScriptedTransport> _transports;

    public ScriptedTransportFactory(IReadOnlyDictionary<string, ScriptedTransport> transports)
    {
        _transports = transports;
    }

    public IUniFiTransport Create(UniFiAccessProfileOptions profile)
    {
        if (!_transports.TryGetValue(profile.Name, out var transport))
        {
            throw new KeyNotFoundException($"No scripted transport registered for profile '{profile.Name}'.");
        }

        return transport;
    }
}

internal sealed class ScriptedTransport : IUniFiTransport
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public ScriptedTransport(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    public int LoginRequestCount { get; private set; }

    public int ApiRequestCount { get; private set; }

    public List<string> CapturedCookies { get; } = [];

    public List<string> ApiKeyHeaders { get; } = [];

    public List<string> AuthorizationHeaders { get; } = [];

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var path = RequestPathHelper.GetPath(request);
        if (path == "/api/auth/login")
        {
            LoginRequestCount++;
        }
        else
        {
            ApiRequestCount++;
            if (request.Headers.TryGetValues("X-API-KEY", out var apiKeyValues))
            {
                ApiKeyHeaders.Add(apiKeyValues.Single());
            }

            if (request.Headers.TryGetValues("Authorization", out var authorizationValues))
            {
                AuthorizationHeaders.Add(authorizationValues.Single());
            }

            if (request.Headers.TryGetValues("Cookie", out var cookieValues))
            {
                CapturedCookies.Add(cookieValues.Single());
            }
        }

        return Task.FromResult(_handler(request));
    }

    public void Dispose()
    {
    }
}

internal static class RequestPathHelper
{
    public static string GetPath(HttpRequestMessage request)
    {
        var requestUri = request.RequestUri;
        if (requestUri is null)
        {
            return string.Empty;
        }

        return requestUri.IsAbsoluteUri
            ? requestUri.PathAndQuery
            : "/" + requestUri.OriginalString.TrimStart('/');
    }
}

internal static class Responses
{
    public static HttpResponseMessage Login(string setCookieHeader)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"authenticated":true}""", Encoding.UTF8, "application/json")
        };

        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookieHeader);
        return response;
    }

    public static HttpResponseMessage Json(HttpRequestMessage request, string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Status(HttpStatusCode statusCode, HttpRequestMessage request, string? json = null)
    {
        return new HttpResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
