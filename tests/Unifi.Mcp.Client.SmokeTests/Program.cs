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
    ("rejects requests outside configured scope", RejectsRequestsOutsideScopeAsync),
    ("keeps authentication errors secret-safe", KeepsAuthenticationErrorsSecretSafeAsync),
    ("loads profile config from JSON", LoadsProfileConfigFromJsonAsync),
    ("supports initialize and tools list", SupportsInitializeAndToolsListAsync),
    ("ignores initialized notifications", IgnoresInitializedNotificationAsync),
    ("returns tool failures as tool results", ReturnsToolFailuresAsToolResultsAsync),
    ("reads and writes stdio frames", ReadsAndWritesFramesAsync)
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
    Ensure(tools.GetArrayLength() == 3, $"Expected three tools, got {tools.GetArrayLength()}.");
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

static async Task ReadsAndWritesFramesAsync()
{
    await using var stream = new MemoryStream();
    const string Payload = """{"jsonrpc":"2.0","id":1}""";

    await McpJsonRpcHost.WriteFrameAsync(stream, Payload).ConfigureAwait(false);
    stream.Position = 0;

    var roundTripped = await McpJsonRpcHost.ReadFrameAsync(stream).ConfigureAwait(false);
    Ensure(roundTripped == Payload, "Expected stdio framing to round-trip unchanged.");
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
    string apiKeyHeaderName = "X-API-KEY")
{
    return new UniFiAccessProfileOptions
    {
        Name = name,
        BaseAddress = new Uri(baseAddress),
        Username = apiKeyEnvironmentVariable is null ? "readonly" : null,
        Password = apiKeyEnvironmentVariable is null ? password : null,
        ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
        ApiKeyHeaderName = apiKeyHeaderName,
        AllowedRelativePathPrefixes = [allowedPrefix]
    };
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

        return requestUri.IsAbsoluteUri ? requestUri.PathAndQuery : requestUri.OriginalString;
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
